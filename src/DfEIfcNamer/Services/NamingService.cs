using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;
using DfEIfcNamer.Utilities;

namespace DfEIfcNamer.Services
{
    public class NamingService
    {
        private readonly CounterStateService _counterService;

        public NamingService(CounterStateService counterService)
        {
            _counterService = counterService;
        }

        public void ApplyTypeNaming(Document doc, IList<TypeRowModel> rows, IList<ClassificationSlot> slots)
        {
            if (rows == null || rows.Count == 0) return;

            var counters = _counterService.LoadCounters(doc);
            using (var tg = new TransactionGroup(doc, "DfE Apply Type IFC Naming"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "Apply Type IFC Naming"))
                {
                    tx.Start();
                    foreach (var row in rows)
                    {
                        var type = doc.GetElement(new ElementId(row.ElementId));
                        if (type == null) continue;

                        var ifcClass = NameFormatting.SafeIfcToken(row.IfcClassToken);
                        var predefined = NameFormatting.NormalizePredefinedType(row.PredefinedType, row.UserDefinedValue);
                        var key = $"TYPE::{ifcClass}::{predefined}";
                        var next = counters.ContainsKey(key) ? counters[key] + 1 : FindMaxTypeSequence(doc, ifcClass, predefined) + 1;
                        counters[key] = next;

                        var ifcTypeName = $"{ifcClass}_{predefined}_Type{next:00}";
                        SetString(type.LookupParameter("IfcName[Type]"), ifcTypeName);
                        SetString(type.LookupParameter("IfcDescription[Type]"), $"{ifcClass} {predefined}");

                        SetString(type.LookupParameter("IFC Export As"), $"Ifc{ifcClass}");
                        SetString(type.LookupParameter("IFC Export Type"), $"Ifc{ifcClass}Type");
                        SetString(type.LookupParameter("IFC Predefined Type"), row.PredefinedType == "USERDEFINED" ? row.UserDefinedValue : row.PredefinedType);

                        foreach (var slot in slots)
                        {
                            SetString(type.LookupParameter(slot.SlotName), $"[{slot.DisplayName}]{{number}}:{{description}}");
                        }
                    }

                    _counterService.SaveCounters(doc, counters);
                    tx.Commit();
                }
                tg.Assimilate();
            }
        }

        public void ApplyInstanceNaming(Document doc, string scope, string numberingMode, IList<IfcEntityDefinition> entities)
        {
            var counters = _counterService.LoadCounters(doc);
            var instanceElements = ResolveScopeElements(doc, scope);
            var maxDigits = instanceElements.Any() ? instanceElements.Max(x => x.Id.IntegerValue).ToString().Length : 6;

            using (var tg = new TransactionGroup(doc, "DfE Apply Instance IFC Naming"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "Apply Instance IFC Naming"))
                {
                    tx.Start();
                    foreach (var element in instanceElements)
                    {
                        var type = doc.GetElement(element.GetTypeId());
                        var ifcExportAs = type?.LookupParameter("IFC Export As")?.AsString() ?? "IfcBuildingElementProxy";
                        var token = ifcExportAs.Replace("Ifc", string.Empty);
                        var entity = entities.FirstOrDefault(x => string.Equals(x.IFCClassToken, token, StringComparison.OrdinalIgnoreCase));
                        var prefix = (entity?.NameFormat ?? "GEN-XXXXX").Replace("XXXXX", string.Empty);

                        string sequence;
                        if (string.Equals(numberingMode, "ElementId", StringComparison.OrdinalIgnoreCase))
                        {
                            sequence = element.Id.IntegerValue.ToString().PadLeft(maxDigits, '0');
                        }
                        else
                        {
                            var key = $"INST::{prefix}";
                            var next = counters.ContainsKey(key) ? counters[key] + 1 : 1;
                            counters[key] = next;
                            sequence = next.ToString().PadLeft(maxDigits, '0');
                        }

                        var ifcName = $"{prefix}{sequence}";
                        SetString(element.LookupParameter("IfcName"), ifcName);
                        SetString(element.LookupParameter("IfcDescription"), "TBD");
                    }

                    _counterService.SaveCounters(doc, counters);
                    tx.Commit();
                }
                tg.Assimilate();
            }
        }

        private static int FindMaxTypeSequence(Document doc, string ifcClass, string predefined)
        {
            var max = 0;
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements();

            foreach (var type in types)
            {
                var existing = type.LookupParameter("IfcName[Type]")?.AsString();
                if (string.IsNullOrWhiteSpace(existing)) continue;

                var prefix = $"{ifcClass}_{predefined}_Type";
                if (!existing.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                var suffix = existing.Substring(prefix.Length);
                if (int.TryParse(suffix, out var value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static IList<Element> ResolveScopeElements(Document doc, string scope)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (scope == "Model" || string.IsNullOrWhiteSpace(scope))
            {
                return collector.Where(x => x.Category != null).ToList();
            }

            return collector.Where(x => x.Category != null).ToList();
        }

        private static void SetString(Parameter parameter, string value)
        {
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value ?? string.Empty);
            }
        }
    }
}
