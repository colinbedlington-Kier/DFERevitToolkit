using System;
using System.Collections.Generic;
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
                _zones = BuiltInZoneCatalog.Default();
            }
            catch (System.IO.FileNotFoundException)
            {
                _zones = new List<ZoneCatalogEntry>();
            }

            try
            {
                _ads = BuiltInAdsClassificationCatalog.Default();
            }
            catch (System.IO.FileNotFoundException)
            {
                _ads = new List<AdsClassificationEntry>();
            }
        }

        public IList<ZoneCatalogEntry> GetZones() => _zones.ToList();
        public IList<AdsClassificationEntry> GetAdsClassifications() => _ads.ToList();

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
                var zone = _zones.FirstOrDefault(z => string.Equals(z.Name, request?.ProposedZoneName, StringComparison.OrdinalIgnoreCase));
                var resolvedAds = ResolveAds(request?.ProposedAdsClassification);
                var proposedAdsClassification = FormatAdsClassification(resolvedAds.Code, resolvedAds.Description);
                var proposedAdsText = resolvedAds.Code;

                result.Rows.Add(new SpaceZonePreviewRow
                {
                    ElementId = element.Id.Value,
                    Category = element.Category?.Name ?? string.Empty,
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

                    var zoneUpdated = _parameterWriter.SetRoomParameter(element, "ZoneName", row.ProposedZoneName ?? string.Empty, result);
                    _parameterWriter.SetRoomParameter(element, "ZoneDescription", row.ProposedZoneDescription ?? string.Empty, result);
                    _parameterWriter.SetRoomParameter(element, "ZoneCategory", row.ProposedZoneCategory ?? string.Empty, result);
                    var resolvedAds = ResolveAds(row.ProposedAdsClassification);
                    var classificationValue = FormatAdsClassification(resolvedAds.Code, resolvedAds.Description);
                    var wroteClassification = _parameterWriter.SetRoomParameter(element, AdsClassificationParameterAliases[0], classificationValue, result);
                    var wroteText = !string.IsNullOrWhiteSpace(resolvedAds.Code) && _parameterWriter.SetRoomParameter(element, AdsTextParameterAliases[0], resolvedAds.Code, result);
                    if (wroteClassification) result.AdsClassificationUpdated++;
                    if (wroteText) result.AdsTextUpdated++;
                    if (!wroteClassification || !wroteText)
                    {
                        result.Logs.Add($"Scope=Room; Target={row.ElementId}; Parameter=DfE ADS Classification; Status=Failed; Reason=ADS write incomplete (classification={wroteClassification}, text={wroteText})");
                    }

                    if (zoneUpdated)
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

        private (string Code, string Description) ResolveAds(string source)
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

            var matched = _ads.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
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
