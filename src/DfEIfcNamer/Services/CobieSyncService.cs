using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class CobieSyncService
    {
        private readonly ParameterService _parameterService;

        public CobieSyncService(ParameterService parameterService)
        {
            _parameterService = parameterService;
        }

        public SetupStatus CheckSetup(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var categories = GetModelCategories(doc, selectedCategoryIds);
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var status = new SetupStatus
            {
                SharedParameterFileFound = System.IO.File.Exists(sharedPath)
            };

            int instanceCoverage;
            status.InstanceParameterBound = IsParameterBound(doc, new[] { "IFCName", "IfcName" }, false, categories, out instanceCoverage);
            int typeCoverage;
            status.TypeParameterBound = IsParameterBound(doc, new[] { "IFCName [Type]", "IFCName[Type]", "IfcName[Type]" }, true, categories, out typeCoverage);
            status.MissingCategoryBindings = Math.Max(categories.Count - instanceCoverage, 0) + Math.Max(categories.Count - typeCoverage, 0);
            status.Message = status.SharedParameterFileFound
                ? "Setup check complete."
                : "Shared parameter file missing. Ensure DfE_IfcNamer_SharedParameters.txt is in add-in folder/Resources.";
            return status;
        }

        public SetupStatus AssignParameters(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var categories = GetModelCategories(doc, selectedCategoryIds);
            _parameterService.EnsureIfcNameParameters(doc, categories);
            return CheckSetup(doc, selectedCategoryIds);
        }

        public IList<ProjectParameterOption> GetStringParameters(Document doc, bool isType)
        {
            var result = new List<ProjectParameterOption>();
            var iterator = doc.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                var definition = iterator.Key as Definition;
                var binding = iterator.Current as ElementBinding;
                if (definition == null || binding == null)
                {
                    continue;
                }

                var isMatchingKind = isType ? binding is TypeBinding : binding is InstanceBinding;
                if (!isMatchingKind)
                {
                    continue;
                }

                if (definition.GetDataType() != SpecTypeId.String.Text)
                {
                    continue;
                }

                result.Add(new ProjectParameterOption { Name = definition.Name, IsType = isType });
            }

            return result
                .Distinct(new NameComparer())
                .OrderBy(x => x.Name)
                .ToList();
        }

        public IList<Category> GetModelCategories(Document doc, IList<ElementId> selected = null)
        {
            var all = doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c != null && c.CategoryType == CategoryType.Model && !c.IsTagCategory && c.AllowsBoundParameters)
                .OrderBy(c => c.Name)
                .ToList();
            if (selected == null || selected.Count == 0)
            {
                return all;
            }

            var set = new HashSet<int>(selected.Select(x => x.IntegerValue));
            return all.Where(c => set.Contains(c.Id.IntegerValue)).ToList();
        }

        public SyncResult ApplySync(Document doc, MappingSettings settings)
        {
            var result = new SyncResult();
            var categories = GetModelCategories(doc, settings.CategoryIds?.Select(id => new ElementId(id)).ToList());
            var instanceCollector = settings.Scope == SyncScope.ActiveView
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            var categoryIds = categories.Select(c => c.Id.IntegerValue).ToHashSet();
            var elements = instanceCollector.WhereElementIsNotElementType().Where(e => e.Category != null && categoryIds.Contains(e.Category.Id.IntegerValue)).ToList();
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Where(e => e.Category != null && categoryIds.Contains(e.Category.Id.IntegerValue))
                .ToList();

            using (var group = new TransactionGroup(doc, "DfE IFC Namer - Sync COBie"))
            {
                group.Start();
                ProcessElements(doc, elements, settings.InstanceSource, settings.InstanceTarget, settings.OverwriteMode, false, result);
                ProcessElements(doc, types, settings.TypeSource, settings.TypeTarget, settings.OverwriteMode, true, result);
                settings.LastSyncUtc = DateTime.UtcNow;
                group.Assimilate();
            }

            return result;
        }

        private static void ProcessElements(Document doc, IList<Element> elements, string sourceParam, string targetParam, OverwriteMode overwriteMode, bool isType, SyncResult result)
        {
            const int chunkSize = 500;
            for (int i = 0; i < elements.Count; i += chunkSize)
            {
                var batch = elements.Skip(i).Take(chunkSize).ToList();
                using (var tx = new Transaction(doc, isType ? "Sync COBie Type" : "Sync COBie Instance"))
                {
                    tx.Start();
                    foreach (var element in batch)
                    {
                        var source = element.LookupParameter(sourceParam);
                        var target = element.LookupParameter(targetParam);
                        if (source == null || target == null)
                        {
                            AddCounters(result, isType, skipped: 1);
                            result.Logs.Add(new SyncLogEntry { Severity = "Warning", Message = $"{element.Id}: Missing source/target parameter." });
                            continue;
                        }

                        if (source.StorageType != StorageType.String || target.StorageType != StorageType.String)
                        {
                            AddCounters(result, isType, failed: 1);
                            result.Logs.Add(new SyncLogEntry { Severity = "Error", Message = $"{element.Id}: Non-string parameter type." });
                            continue;
                        }

                        var sourceValue = source.AsString();
                        var targetValue = target.AsString();
                        if (string.IsNullOrWhiteSpace(sourceValue))
                        {
                            AddCounters(result, isType, skipped: 1);
                            continue;
                        }

                        if (overwriteMode == OverwriteMode.BlankOnly && !string.IsNullOrWhiteSpace(targetValue))
                        {
                            AddCounters(result, isType, skipped: 1);
                            continue;
                        }

                        if (target.IsReadOnly)
                        {
                            AddCounters(result, isType, failed: 1);
                            result.Logs.Add(new SyncLogEntry { Severity = "Error", Message = $"{element.Id}: target is read-only." });
                            continue;
                        }

                        target.Set(sourceValue);
                        AddCounters(result, isType, updated: 1);
                    }

                    tx.Commit();
                }
            }
        }

        private static void AddCounters(SyncResult result, bool isType, int updated = 0, int skipped = 0, int failed = 0)
        {
            if (isType)
            {
                result.TypesUpdated += updated;
                result.TypesSkipped += skipped;
                result.TypesFailed += failed;
            }
            else
            {
                result.InstancesUpdated += updated;
                result.InstancesSkipped += skipped;
                result.InstancesFailed += failed;
            }
        }

        private static bool IsParameterBound(Document doc, IEnumerable<string> parameterNames, bool isType, IList<Category> categories, out int coverage)
        {
            coverage = 0;
            var acceptedNames = new HashSet<string>(parameterNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var iterator = doc.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                if (!(iterator.Key is Definition definition) || !acceptedNames.Contains(definition.Name))
                {
                    continue;
                }

                var binding = iterator.Current as ElementBinding;
                if (binding == null)
                {
                    return false;
                }

                var bound = binding.Categories.Cast<Category>().Select(c => c.Id.IntegerValue).ToHashSet();
                coverage = categories.Count(c => bound.Contains(c.Id.IntegerValue));
                return (isType && binding is TypeBinding) || (!isType && binding is InstanceBinding);
            }

            return false;
        }

        private class NameComparer : IEqualityComparer<ProjectParameterOption>
        {
            public bool Equals(ProjectParameterOption x, ProjectParameterOption y) => string.Equals(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode(ProjectParameterOption obj) => (obj.Name ?? string.Empty).ToLowerInvariant().GetHashCode();
        }
    }
}
