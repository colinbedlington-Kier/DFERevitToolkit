using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class SpaceZoneService
    {
        public const string AdsTextParameterName = "DfE ADS Code";
        private static readonly string[] AdsTextParameterAliases = { AdsTextParameterName, "DfE ADS", "ADS Code", "ADS" };
        private static readonly string[] AdsClassificationParameterAliases = { "DfE ADS Classification" };
        private readonly IList<ZoneCatalogEntry> _zones;
        private readonly IList<AdsClassificationEntry> _ads;
        private readonly ParameterWriteService _parameterWriter = new ParameterWriteService();

        public SpaceZoneService()
        {
            try
            {
                _zones = BuiltInZoneCatalog.Default("/mnt/data/DfeZoneCatalog.csv");
                Debug.WriteLine($"[DfEIfcNamer] Zone catalog load count: {_zones.Count}");
            }
            catch (System.IO.FileNotFoundException)
            {
                _zones = new List<ZoneCatalogEntry>();
            }

            try
            {
                _ads = BuiltInAdsClassificationCatalog.Default("/mnt/data/DfeAdsCatalog.csv");
                Debug.WriteLine($"[DfEIfcNamer] ADS catalog load count: {_ads.Count}");
            }
            catch (System.IO.FileNotFoundException)
            {
                _ads = new List<AdsClassificationEntry>();
            }
        }

        public IList<ZoneCatalogEntry> GetZones(Document doc = null)
        {
            var merged = _zones.ToList();
            foreach (var modelZone in GetModelZones(doc))
            {
                if (!merged.Any(x => string.Equals(x.Name, modelZone.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(modelZone);
                }
            }

            return merged
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IList<AdsClassificationEntry> GetAdsClassifications(Document doc = null)
        {
            var merged = _ads.ToList();
            foreach (var modelAds in GetModelAds(doc))
            {
                if (!merged.Any(x => string.Equals(x.Code, modelAds.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(modelAds);
                }
            }

            return merged
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public SpaceZonePreviewResult BuildPreview(Document doc, SpaceZoneRequest request)
        {
            var result = new SpaceZonePreviewResult();
            var elements = ResolveElements(doc, request?.ElementIds, out var skippedNonRoomSpace);
            result.SelectedCount = elements.Count;
            result.SkippedNonRoomSpaceCount = skippedNonRoomSpace;
            result.ValidRoomSpaceCount = elements.Count;
            foreach (var element in elements)
            {
                var room = element as Room;
                var roomNumber = room?.Number ?? Get(element, "Number");
                var roomName = room?.Name ?? Get(element, "Name");
                if (string.IsNullOrWhiteSpace(roomNumber)) result.MissingRoomCount++;
                var zones = GetZones(doc);
                var adsCatalog = GetAdsClassifications(doc);
                var zone = zones.FirstOrDefault(z => string.Equals(z.Name, request?.ProposedZoneName, StringComparison.OrdinalIgnoreCase));
                var resolvedAds = ResolveAds(request?.ProposedAdsClassification, adsCatalog);
                var proposedAdsClassification = FormatAdsClassification(resolvedAds.Code, resolvedAds.Description);
                var proposedAdsText = resolvedAds.Code;

                result.Rows.Add(new SpaceZonePreviewRow
                {
                    ElementId = element.Id.Value,
                    Category = element.Category?.Name ?? string.Empty,
                    Family = GetFamily(doc, element),
                    Type = GetType(doc, element),
                    FamilyType = BuildFamilyType(doc, element),
                    Level = doc.GetElement(element.LevelId)?.Name ?? string.Empty,
                    RoomNumber = roomNumber,
                    RoomName = roomName,
                    CurrentSpaceReference = Get(element, "SpaceReference"),
                    ProposedSpaceReference = string.IsNullOrWhiteSpace(roomNumber) ? string.Empty : roomNumber + (string.IsNullOrWhiteSpace(roomName) ? string.Empty : " - " + roomName),
                    CurrentZoneName = Get(element, "ZoneName"),
                    ProposedZoneName = request?.ProposedZoneName ?? string.Empty,
                    CurrentZoneDescription = Get(element, "ZoneDescription"),
                    ProposedZoneDescription = zone?.Description ?? string.Empty,
                    CurrentZoneCategory = Get(element, "ZoneCategory"),
                    ProposedZoneCategory = zone?.Category ?? string.Empty,
                    CurrentAdsClassification = Get(element, AdsClassificationParameterAliases),
                    ProposedAdsClassification = proposedAdsClassification,
                    CurrentAdsText = Get(element, AdsTextParameterAliases),
                    ProposedAdsText = proposedAdsText,
                    Status = string.IsNullOrWhiteSpace(roomNumber) ? "Missing room/space" : "OK"
                });
            }

            return result;
        }

        public ApplyResult ApplySpaceReference(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Apply SpaceReference"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<SpaceZonePreviewRow>())
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    if (!ParameterWriteService.IsRoom(element))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=SpaceReference; Status=Skipped; Reason=Skipped non-room element for room-only parameter");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(row.ProposedSpaceReference))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=SpaceReference; Status=Skipped; Reason=null value");
                        continue;
                    }

                    if (_parameterWriter.SetRoomParameter(element, "SpaceReference", row.ProposedSpaceReference, result))
                    {
                        result.Updated++;
                    }
                }

                tx.Commit();
            }

            return result;
        }

        public ApplyResult ApplyZone(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Apply ZoneName"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<SpaceZonePreviewRow>())
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    if (!ParameterWriteService.IsRoom(element))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=ZoneName; Status=Skipped; Reason=Skipped non-room element for room-only parameter");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(row.ProposedZoneName))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=ZoneName; Status=Skipped; Reason=null value");
                        continue;
                    }

                    var catalogZone = GetZones(doc).FirstOrDefault(z => string.Equals(z.Name, row.ProposedZoneName, StringComparison.OrdinalIgnoreCase));
                    var zoneUpdated = _parameterWriter.SetRoomParameter(element, "ZoneName", row.ProposedZoneName ?? string.Empty, result);
                    _parameterWriter.SetRoomParameter(element, "ZoneDescription", catalogZone?.Description ?? row.ProposedZoneDescription ?? string.Empty, result);
                    _parameterWriter.SetRoomParameter(element, "ZoneCategory", catalogZone?.Category ?? row.ProposedZoneCategory ?? string.Empty, result);

                    if (zoneUpdated)
                    {
                        result.Updated++;
                    }
                }

                tx.Commit();
            }

            return result;
        }

        public ApplyResult ApplyAds(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Apply ADS"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<SpaceZonePreviewRow>())
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    if (!ParameterWriteService.IsRoom(element))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=DfE ADS Classification; Status=Skipped; Reason=Skipped non-room element for room-only parameter");
                        continue;
                    }

                    var resolvedAds = ResolveAds(row.ProposedAdsClassification, GetAdsClassifications(doc));
                    if (string.IsNullOrWhiteSpace(resolvedAds.Code))
                    {
                        result.Skipped++;
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=DfE ADS Classification; Status=Skipped; Reason=null value");
                        continue;
                    }

                    var classificationValue = FormatAdsClassification(resolvedAds.Code, resolvedAds.Description);
                    var wroteClassification = _parameterWriter.SetRoomParameter(element, AdsClassificationParameterAliases[0], classificationValue, result);
                    var wroteText = _parameterWriter.SetRoomParameter(element, AdsTextParameterAliases[0], resolvedAds.Code, result);
                    if (wroteClassification) result.AdsClassificationUpdated++;
                    if (wroteText) result.AdsTextUpdated++;
                    if (wroteClassification || wroteText)
                    {
                        result.Updated++;
                    }
                }
                tx.Commit();
            }
            return result;
        }

        public Room ResolveRoom(Document doc, Element element)
        {
            if (element is FamilyInstance fi)
            {
                var room = fi.Room ?? fi.ToRoom ?? fi.FromRoom;
                if (room != null) return room;
            }

            var locationPoint = (element.Location as LocationPoint)?.Point;
            if (locationPoint != null)
            {
                return doc.GetRoomAtPoint(locationPoint);
            }

            return null;
        }

        private static IList<Element> ResolveElements(Document doc, IEnumerable<long> ids, out int skippedNonRoomSpace)
        {
            skippedNonRoomSpace = 0;
            Func<Element, bool> valid = e =>
            {
                var bic = (BuiltInCategory)e.Category.Id.Value;
                return bic == BuiltInCategory.OST_Rooms;
            };
            if (ids != null && ids.Any())
            {
                var selected = ids.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null && e.Category != null).ToList();
                skippedNonRoomSpace = selected.Count(e => !valid(e));
                return selected.Where(valid).ToList();
            }

            var all = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.Category != null).Take(5000).ToList();
            skippedNonRoomSpace = all.Count(e => !valid(e));
            return all.Where(valid).ToList();
        }

        private static string BuildFamilyType(Document doc, Element element)
        {
            var symbol = doc.GetElement(element.GetTypeId()) as ElementType;
            var family = symbol?.FamilyName ?? string.Empty;
            var type = symbol?.Name ?? string.Empty;
            return (family + " / " + type).Trim(' ', '/');
        }

        private static string GetFamily(Document doc, Element element)
        {
            var symbol = doc.GetElement(element.GetTypeId()) as ElementType;
            return symbol?.FamilyName ?? string.Empty;
        }

        private static string GetType(Document doc, Element element)
        {
            var symbol = doc.GetElement(element.GetTypeId()) as ElementType;
            return symbol?.Name ?? string.Empty;
        }

        private static string Get(Element element, params string[] parameterNames)
        {
            foreach (var parameterName in parameterNames ?? Array.Empty<string>())
            {
                var value = element?.LookupParameter(parameterName)?.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private IList<ZoneCatalogEntry> GetModelZones(Document doc)
        {
            if (doc == null) return new List<ZoneCatalogEntry>();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => ParameterWriteService.IsRoom(e))
                .Select(e => new ZoneCatalogEntry
                {
                    Name = Get(e, "ZoneName"),
                    Description = Get(e, "ZoneDescription"),
                    Category = Get(e, "ZoneCategory")
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private IList<AdsClassificationEntry> GetModelAds(Document doc)
        {
            if (doc == null) return new List<AdsClassificationEntry>();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => ParameterWriteService.IsRoom(e))
                .Select(e => Get(e, AdsClassificationParameterAliases))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(ResolveAds)
                .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .Select(x => new AdsClassificationEntry { Code = x.Code, Description = x.Description })
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private (string Code, string Description) ResolveAds(string source)
        {
            return ResolveAds(source, _ads);
        }

        private (string Code, string Description) ResolveAds(string source, IEnumerable<AdsClassificationEntry> adsSource)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return (string.Empty, string.Empty);
            }

            var payload = source.Trim();
            if (payload.StartsWith("[DfE ADS Classification]", StringComparison.OrdinalIgnoreCase))
            {
                payload = payload.Substring("[DfE ADS Classification]".Length).Trim();
            }

            var code = payload;
            if (payload.Contains(" - "))
            {
                code = payload.Split(new[] { " - " }, 2, StringSplitOptions.None)[0].Trim();
            }
            else if (payload.Contains(" : "))
            {
                code = payload.Split(new[] { " : " }, 2, StringSplitOptions.None)[0].Trim();
            }

            var matched = (adsSource ?? _ads).FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            var description = matched?.Description;
            if (string.IsNullOrWhiteSpace(description) && payload.Contains(" - "))
            {
                description = payload.Split(new[] { " - " }, 2, StringSplitOptions.None)[1].Trim();
            }
            else if (string.IsNullOrWhiteSpace(description) && payload.Contains(" : "))
            {
                description = payload.Split(new[] { " : " }, 2, StringSplitOptions.None)[1].Trim();
            }

            return (code, description ?? string.Empty);
        }

        private static string FormatAdsClassification(string code, string description)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var formatted = "[DfE ADS Classification] " + code.Trim();
            if (!string.IsNullOrWhiteSpace(description))
            {
                formatted += " : " + description.Trim();
            }

            return formatted;
        }
    }
}
