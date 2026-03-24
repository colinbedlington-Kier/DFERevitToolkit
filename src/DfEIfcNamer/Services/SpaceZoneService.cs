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
        public SpaceZonePreviewResult BuildPreview(Document doc, SpaceZoneRequest request)
        {
            var result = new SpaceZonePreviewResult();
            var elements = ResolveElements(doc, request?.ElementIds);
            result.SelectedCount = elements.Count;
            foreach (var element in elements)
            {
                var room = ResolveRoom(doc, element);
                var roomNumber = room?.Number ?? string.Empty;
                var roomName = room?.Name ?? string.Empty;
                if (room == null) result.MissingRoomCount++;

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
                    Status = room == null ? "Missing room/space" : "OK"
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

        private static IList<Element> ResolveElements(Document doc, IEnumerable<long> ids)
        {
            if (ids != null && ids.Any())
            {
                return ids.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
            }

            return new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.Category != null).Take(5000).ToList();
        }

        private static string BuildFamilyType(Document doc, Element element)
        {
            var symbol = doc.GetElement(element.GetTypeId()) as ElementType;
            var family = symbol?.FamilyName ?? string.Empty;
            var type = symbol?.Name ?? string.Empty;
            return (family + " / " + type).Trim(' ', '/');
        }

        private static string Get(Element element, string parameterName) => element?.LookupParameter(parameterName)?.AsString() ?? string.Empty;
    }
}
