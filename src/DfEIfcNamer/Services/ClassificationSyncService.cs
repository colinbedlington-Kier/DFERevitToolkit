using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ClassificationSyncService
    {
        private readonly InstanceParameterWriter _instanceWriter = new InstanceParameterWriter();

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

                var prNumber = Get(type, "Classification.Uniclass.Pr.Number");
                var prDescription = Get(type, "Classification.Uniclass.Pr.Description");
                var ssNumber = Get(type, "Classification.Uniclass.Ss.Number");
                var ssDescription = Get(type, "Classification.Uniclass.Ss.Description");
                var enName = Get(type, "Uniclass.Classification.En.Name");
                var efName = Get(type, "Uniclass.Classification.EF.Name");
                if (string.IsNullOrWhiteSpace(prNumber) &&
                    string.IsNullOrWhiteSpace(ssNumber) &&
                    string.IsNullOrWhiteSpace(enName) &&
                    string.IsNullOrWhiteSpace(efName)) continue;

                var proposedC2 = !string.IsNullOrWhiteSpace(enName)
                    ? $"[Uniclass En Entities] {enName}".Trim()
                    : (string.IsNullOrWhiteSpace(prNumber) ? string.Empty : $"[Uniclass Pr Products] {prNumber} : {prDescription}".Trim());
                var proposedC3 = !string.IsNullOrWhiteSpace(efName)
                    ? $"[Uniclass EF Elements/Functions] {efName}".Trim()
                    : (string.IsNullOrWhiteSpace(ssNumber) ? string.Empty : $"[Uniclass Ss Systems] {ssNumber} : {ssDescription}".Trim());

                result.Rows.Add(new ClassificationSyncPreviewRow
                {
                    ElementId = element.Id.Value,
                    TypeElementId = type.Id.Value,
                    Category = element.Category.Name,
                    SourcePrNumber = prNumber,
                    SourcePrDescription = prDescription,
                    SourceSsNumber = ssNumber,
                    SourceSsDescription = ssDescription,
                    SourceClassificationEnName = enName,
                    SourceClassificationEfName = efName,
                    ProposedClassification2 = proposedC2,
                    ProposedClassification3 = proposedC3,
                    Scope = "Type->Instance",
                    Status = "Ready"
                });
            }

            if (!elements.Any(e => doc.GetElement(e.GetTypeId())?.LookupParameter("Uniclass.Classification.En.Name") != null))
                result.Warnings.Add("Missing source parameter on scanned types: Uniclass.Classification.En.Name (Classification(2) fallback used where available).");
            if (!elements.Any(e => doc.GetElement(e.GetTypeId())?.LookupParameter("Uniclass.Classification.EF.Name") != null))
                result.Warnings.Add("Missing source parameter on scanned types: Uniclass.Classification.EF.Name (Classification(3) fallback used where available).");

            result.SourceRows = result.Rows.Count;
            result.TypeTargets = result.Rows.Select(x => x.TypeElementId).Distinct().Count();
            result.InstanceTargets = result.Rows.Select(x => x.ElementId).Distinct().Count();
            return result;
        }

        public ApplyResult Apply(Document doc, IEnumerable<ClassificationSyncPreviewRow> rows)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Classification Sync"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<ClassificationSyncPreviewRow>())
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    if (element == null) { result.Skipped++; continue; }

                    var wrote2 = _instanceWriter.Write(element, row.ProposedClassification2, "Classification(2)");
                    var wrote3 = _instanceWriter.Write(element, row.ProposedClassification3, "Classification(3)");
                    if (!wrote2 && !wrote3) { result.Skipped++; continue; }

                    result.InstancesUpdated++;
                    result.Updated++;
                }
                tx.Commit();
            }

            return result;
        }

        private static string Get(Element element, string name) => element?.LookupParameter(name)?.AsString() ?? string.Empty;
    }
}
