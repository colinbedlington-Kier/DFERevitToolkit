using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ClassificationSyncService
    {
        public ClassificationSyncResult BuildPreview(Document doc, IList<long> categoryIds)
        {
            var result = new ClassificationSyncResult();
            var selected = categoryIds?.ToHashSet() ?? new HashSet<long>();
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && (selected.Count == 0 || selected.Contains(e.Category.Id.Value)))
                .ToList();

            foreach (var element in elements)
            {
                var type = doc.GetElement(element.GetTypeId());
                if (type == null) continue;
                var c1 = Get(type, "Classification");
                var c2 = Get(type, "Classification(2)");
                if (string.IsNullOrWhiteSpace(c1) && string.IsNullOrWhiteSpace(c2)) continue;

                var pr = Parse(c1);
                var ss = Parse(c2);
                result.Rows.Add(new ClassificationSyncPreviewRow
                {
                    ElementId = element.Id.Value,
                    TypeElementId = type.Id.Value,
                    Category = element.Category.Name,
                    SourceClassification = c1,
                    SourceClassification2 = c2,
                    ProposedPrNumber = pr.number,
                    ProposedPrDescription = pr.description,
                    ProposedSsNumber = ss.number,
                    ProposedSsDescription = ss.description,
                    Scope = "Type->Pr, Instance->Ss",
                    Status = "Ready"
                });
            }

            result.SourceRows = result.Rows.Count;
            result.TypeTargets = result.Rows.Select(x => x.TypeElementId).Distinct().Count();
            result.InstanceTargets = result.Rows.Select(x => x.ElementId).Distinct().Count();
            return result;
        }

        public ApplyResult Apply(Document doc, IEnumerable<ClassificationSyncPreviewRow> rows)
        {
            var result = new ApplyResult();
            var typeDone = new HashSet<long>();
            using (var tx = new Transaction(doc, "DfE Classification Sync"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<ClassificationSyncPreviewRow>())
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    var type = doc.GetElement(new ElementId(row.TypeElementId));
                    if (element == null || type == null) { result.Skipped++; continue; }

                    if (typeDone.Add(row.TypeElementId))
                    {
                        Set(type, row.ProposedPrNumber, "Classification.Uniclass.Pr.Number");
                        Set(type, row.ProposedPrDescription, "Classification.Uniclass.Pr.Description");
                        result.UniqueTypesUpdated++;
                    }

                    Set(element, row.ProposedSsNumber, "Classification.Uniclass.Ss.Number");
                    Set(element, row.ProposedSsDescription, "Classification.Uniclass.Ss.Description");
                    result.InstancesUpdated++;
                    result.Updated++;
                }
                tx.Commit();
            }

            return result;
        }

        private static (string number, string description) Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return (string.Empty, string.Empty);
            var chunks = input.Split(new[] { '|' }, 2);
            if (chunks.Length == 2) return (chunks[0].Trim(), chunks[1].Trim());
            var parts = input.Split(new[] { ' ' }, 2);
            return parts.Length == 2 ? (parts[0].Trim(), parts[1].Trim()) : (input.Trim(), string.Empty);
        }

        private static string Get(Element element, string name) => element?.LookupParameter(name)?.AsString() ?? string.Empty;

        private static void Set(Element element, string value, string name)
        {
            var p = element?.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(value ?? string.Empty);
        }
    }
}
