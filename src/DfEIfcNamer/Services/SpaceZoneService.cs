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
        private readonly IList<ZoneCatalogEntry> _zones;
        private readonly IList<AdsClassificationEntry> _ads;
        private readonly InstanceParameterWriter _instanceWriter = new InstanceParameterWriter();

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
                    CurrentAdsClassification = Get(element, "DfE ADS Classification"),
                    ProposedAdsClassification = FormatAdsClassification(request?.ProposedAdsClassification),
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
                    var p = element?.LookupParameter("SpaceReference");
                    if (p == null || p.IsReadOnly)
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(row.ProposedSpaceReference))
                    {
                        result.Skipped++;
                        continue;
                    }

                    p.Set(row.ProposedSpaceReference);
                    result.Updated++;
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
                    var p = element?.LookupParameter("ZoneName");
                    if (p == null || p.IsReadOnly)
                    {
                        result.Skipped++;
                        continue;
                    }

                    p.Set(row.ProposedZoneName ?? string.Empty);
                    _instanceWriter.Write(element, row.ProposedZoneDescription, "ZoneDescription");
                    _instanceWriter.Write(element, row.ProposedZoneCategory, "ZoneCategory");
                    _instanceWriter.Write(element, FormatAdsClassification(row.ProposedAdsClassification), "DfE ADS Classification");
                    result.Updated++;
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
                return bic == BuiltInCategory.OST_Rooms || bic == BuiltInCategory.OST_MEPSpaces;
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

        private static string Get(Element element, string parameterName) => element?.LookupParameter(parameterName)?.AsString() ?? string.Empty;

        private static string FormatAdsClassification(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;
            if (source.StartsWith("[DfE ADS Classification]", StringComparison.OrdinalIgnoreCase)) return source;
            return "[DfE ADS Classification] " + source.Trim();
        }
        private static void Set(Element element, string value, string parameterName)
        {
            var p = element?.LookupParameter(parameterName);
            if (p != null && !p.IsReadOnly) p.Set(value ?? string.Empty);
        }
    }
}
