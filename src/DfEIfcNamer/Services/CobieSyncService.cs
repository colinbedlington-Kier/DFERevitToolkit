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
        private readonly ResourceJsonService _resourceJsonService;

        public CobieSyncService(ParameterService parameterService, ResourceJsonService resourceJsonService)
        {
            _parameterService = parameterService;
            _resourceJsonService = resourceJsonService;
        }

        public SetupStatus CheckSetup(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var status = BuildStatusSkeleton();

            try
            {
                var categories = GetModelCategories(doc, selectedCategoryIds, out var skippedUnsupported);
                status.IncludedCategoriesCount = categories.Count;
                status.SkippedUnsupportedCategoriesCount = skippedUnsupported;
                status.IncludedCategoryNames = categories.Select(c => c.Name).ToList();

                status.SharedParameterFileFound = System.IO.File.Exists(status.SharedParameterFilePath);
                status.EntityMappingFileExists = System.IO.File.Exists(status.EntityMappingJsonPath);
                status.ClassificationSlotsFileExists = System.IO.File.Exists(status.ClassificationSlotsJsonPath);
                status.EntityMappingLoaded = TryLoad(() => _resourceJsonService.LoadEntityLibrary(), out var entityError);
                status.ClassificationSlotsLoaded = TryLoad(() => _resourceJsonService.LoadClassificationSlots(), out var classificationError);

                status.InstanceParameterBound = IsParameterBound(doc, new[] { "IFCName", "IfcName" }, false, categories);
                status.TypeParameterBound = IsParameterBound(doc, new[] { "IFCName [Type]", "IFCName[Type]", "IfcName[Type]" }, true, categories);

                status.Message = status.SharedParameterFileFound && status.EntityMappingLoaded && status.ClassificationSlotsLoaded
                    ? "Setup check complete."
                    : "Setup check completed with missing resources.";

                var errors = new List<string>();
                if (!status.SharedParameterFileFound)
                {
                    errors.Add("Shared parameter file missing: " + status.SharedParameterFilePath);
                }

                if (!status.EntityMappingLoaded && !string.IsNullOrWhiteSpace(entityError))
                {
                    errors.Add(entityError);
                }

                if (!status.ClassificationSlotsLoaded && !string.IsNullOrWhiteSpace(classificationError))
                {
                    errors.Add(classificationError);
                }

                status.ErrorDetails = string.Join(" | ", errors);
            }
            catch (Exception ex)
            {
                status.Message = "Setup check failed.";
                status.ErrorDetails = ex.Message;
            }

            return status;
        }

        public SetupStatus AssignParameters(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var selectedCategories = selectedCategoryIds?.Select(id => Category.GetCategory(doc, id)).Where(c => c != null).ToList();
            var bindingSummary = _parameterService.EnsureIfcNameParameters(doc, selectedCategories);
            var status = CheckSetup(doc, selectedCategoryIds);
            status.FailedBindingInsertCount = bindingSummary.FailedBindingInsertCount;
            status.IncludedCategoriesCount = bindingSummary.IncludedCategoriesCount;
            status.SkippedUnsupportedCategoriesCount = bindingSummary.SkippedUnsupportedCategoriesCount;
            status.IncludedCategoryNames = bindingSummary.IncludedCategoryNames.ToList();

            if (!string.IsNullOrWhiteSpace(bindingSummary.ErrorMessage))
            {
                status.Message = "Assign parameters completed with errors.";
                status.ErrorDetails = bindingSummary.ErrorMessage;
            }

            return status;
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
            return GetModelCategories(doc, selected, out _);
        }

        public SyncResult ApplySync(Document doc, MappingSettings settings)
        {
            var result = new SyncResult();
            var categories = GetModelCategories(doc, settings.CategoryIds?.Select(id => new ElementId(id)).ToList());
            var instanceCollector = settings.Scope == SyncScope.ActiveView
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            var categoryIds = new HashSet<long>(categories.Select(c => c.Id.Value));
            var elements = instanceCollector.WhereElementIsNotElementType().Where(e => e.Category != null && categoryIds.Contains(e.Category.Id.Value)).ToList();
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Where(e => e.Category != null && categoryIds.Contains(e.Category.Id.Value))
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

        private SetupStatus BuildStatusSkeleton()
        {
            return new SetupStatus
            {
                ResolvedAddinFolder = _parameterService.ResolveAddinFolder(),
                SharedParameterFilePath = _parameterService.ResolveSharedParameterFilePath(),
                EntityMappingJsonPath = _resourceJsonService.ResolveEntityMappingPath(),
                ClassificationSlotsJsonPath = _resourceJsonService.ResolveClassificationSlotsPath()
            };
        }

        private static bool TryLoad<T>(Func<IList<T>> loader, out string error)
        {
            try
            {
                var items = loader();
                error = null;
                return items != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static IList<Category> GetModelCategories(Document doc, IList<ElementId> selected, out int skippedUnsupported)
        {
            skippedUnsupported = 0;
            var selectedIds = selected == null || selected.Count == 0
                ? null
                : new HashSet<long>(selected.Select(x => x.Value));

            var valid = new List<Category>();
            foreach (var category in doc.Settings.Categories.Cast<Category>().Where(c => c != null).OrderBy(c => c.Name))
            {
                if (selectedIds != null && !selectedIds.Contains(category.Id.Value))
                {
                    continue;
                }

                if (!category.AllowsBoundParameters || category.IsTagCategory || category.CategoryType == CategoryType.Internal)
                {
                    skippedUnsupported++;
                    continue;
                }

                try
                {
                    valid.Add(category);
                }
                catch
                {
                    skippedUnsupported++;
                }
            }

            return valid;
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

        private static bool IsParameterBound(Document doc, IEnumerable<string> parameterNames, bool isType, IList<Category> categories)
        {
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

                var bound = new HashSet<long>(binding.Categories.Cast<Category>().Select(c => c.Id.Value));
                var coverage = categories.Count(c => bound.Contains(c.Id.Value));
                if (coverage == 0 && categories.Count > 0)
                {
                    return false;
                }

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
