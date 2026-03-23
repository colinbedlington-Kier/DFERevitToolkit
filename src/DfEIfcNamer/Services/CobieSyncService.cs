using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class CobieSyncService
    {
        private static readonly ParameterExpectation[] ExpectedParameters =
        {
            new ParameterExpectation("IfcName", "instance", false, "IfcName", "IFCName"),
            new ParameterExpectation("IfcDescription", "instance", false, "IfcDescription"),
            new ParameterExpectation("DfE_IFCPredefinedType", "instance", false, "DfE_IFCPredefinedType"),
            new ParameterExpectation("DfE_UserDefinedPredefinedTypeValue", "instance", false, "DfE_UserDefinedPredefinedTypeValue"),
            new ParameterExpectation("DfE_IFCEntity", "instance", false, "DfE_IFCEntity"),
            new ParameterExpectation("IfcName[Type]", "type", false, "IfcName[Type]", "IFCName[Type]", "IFCName [Type]"),
            new ParameterExpectation("IfcDescription[Type]", "type", false, "IfcDescription[Type]"),
            new ParameterExpectation("Classification", "type", false, "Classification"),
            new ParameterExpectation("Classification(2)", "type", false, "Classification(2)"),
            new ParameterExpectation("Classification(3)", "type", false, "Classification(3)"),
            new ParameterExpectation("Classification(4)", "type", false, "Classification(4)"),
            new ParameterExpectation("Classification(5)", "type", false, "Classification(5)"),
            new ParameterExpectation("Classification(6)", "type", false, "Classification(6)"),
            new ParameterExpectation("Classification(7)", "type", false, "Classification(7)"),
            new ParameterExpectation("Classification(8)", "type", false, "Classification(8)"),
            new ParameterExpectation("Classification(9)", "type", false, "Classification(9)"),
            new ParameterExpectation("DfE_ProjectInfoJson", "project info", true, "DfE_ProjectInfoJson"),
            new ParameterExpectation("DfE_NamingCounters", "project info", true, "DfE_NamingCounters")
        };

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
                status.ParameterResults = BuildVerificationResults(doc, categories).ToList();
                status.ParametersRequestedCount = status.ParameterResults.Count;
                status.VerifiedBoundCount = status.ParameterResults.Count(x => x.FinalBoundState);
                status.VerificationFailedCount = status.ParameterResults.Count(x => !x.FinalBoundState);

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
                var resourcesOk = status.SharedParameterFileFound &&
                                  status.EntityMappingLoaded &&
                                  status.ClassificationSlotsLoaded &&
                                  string.IsNullOrWhiteSpace(status.ErrorDetails);
                status.Message = resourcesOk ? "Resource diagnostics: OK" : "Resource diagnostics: Error";
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
            status.ParametersRequestedCount = bindingSummary.ParametersRequestedCount;
            status.ParametersFoundInSharedFileCount = bindingSummary.ParametersFoundInSharedFileCount;
            status.InsertSucceededCount = bindingSummary.InsertSucceededCount;
            status.ReInsertSucceededCount = bindingSummary.ReInsertSucceededCount;
            status.VerifiedBoundCount = bindingSummary.VerifiedBoundCount;
            status.VerificationFailedCount = bindingSummary.VerificationFailedCount;
            status.ParameterResults = bindingSummary.ParameterResults.ToList();

            if (!string.IsNullOrWhiteSpace(bindingSummary.ErrorMessage))
            {
                status.Message = BuildParameterSummaryMessage(status, false, hasErrors: true);
                status.ErrorDetails = bindingSummary.ErrorMessage;
            }
            else if (status.VerificationFailedCount == 0 && status.ParametersRequestedCount > 0)
            {
                status.Message = BuildParameterSummaryMessage(status, true, hasErrors: false);
                status.ErrorDetails = string.Empty;
            }
            else
            {
                status.Message = BuildParameterSummaryMessage(status, true, hasErrors: true);
                status.ErrorDetails = string.Empty;
            }

            return status;
        }

        private static string BuildParameterSummaryMessage(SetupStatus status, bool sharedParameterLoaded, bool hasErrors)
        {
            var errorCount = hasErrors
                ? status.VerificationFailedCount + status.FailedBindingInsertCount + (string.IsNullOrWhiteSpace(status.ErrorDetails) ? 0 : 1)
                : 0;

            return $"Shared parameter file loaded: {(sharedParameterLoaded ? "yes" : "no")} | Bound: {status.ParametersRequestedCount} | Verified: {status.VerifiedBoundCount} | Errors: {errorCount}";
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

        private static IList<ParameterBindingResult> BuildVerificationResults(Document doc, IList<Category> modelCategories)
        {
            var results = new List<ParameterBindingResult>();
            var bindingMap = GetBindingMap(doc);
            var projectInfoCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);

            foreach (var expected in ExpectedParameters)
            {
                var result = new ParameterBindingResult
                {
                    Name = expected.DisplayName,
                    ExpectedBindingType = expected.ExpectedBindingType,
                    FoundInSharedParameterFile = true,
                    BindingAction = "Verify"
                };

                if (!TryResolveBinding(bindingMap, expected.LookupNames, out var definitionName, out var binding))
                {
                    result.FinalBoundState = false;
                    result.Notes = "Definition not bound in document.";
                    results.Add(result);
                    continue;
                }

                var kindOk = expected.ExpectedBindingType == "type"
                    ? binding is TypeBinding
                    : binding is InstanceBinding;

                var categoriesOk = expected.ProjectInfo
                    ? BindingContainsCategory(binding, projectInfoCategory)
                    : BindingCoversCategories(binding, modelCategories);

                result.FinalBoundState = kindOk && categoriesOk;
                if (!kindOk)
                {
                    result.Notes = $"Binding kind mismatch for definition '{definitionName}'.";
                }
                else if (!categoriesOk)
                {
                    result.Notes = "Binding categories do not match expected scope.";
                }

                results.Add(result);
            }

            return results;
        }

        private static Dictionary<string, ElementBinding> GetBindingMap(Document doc)
        {
            var map = new Dictionary<string, ElementBinding>(StringComparer.OrdinalIgnoreCase);
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

                map[definition.Name] = binding;
            }

            return map;
        }

        private static bool TryResolveBinding(
            Dictionary<string, ElementBinding> map,
            IEnumerable<string> lookupNames,
            out string definitionName,
            out ElementBinding binding)
        {
            foreach (var candidate in lookupNames)
            {
                if (map.TryGetValue(candidate, out binding))
                {
                    definitionName = candidate;
                    return true;
                }
            }

            definitionName = null;
            binding = null;
            return false;
        }

        private static bool BindingCoversCategories(ElementBinding binding, IList<Category> expectedCategories)
        {
            var actual = new HashSet<long>(
                binding.Categories
                    .Cast<Category>()
                    .Where(c => c != null)
                    .Select(c => c.Id.Value));

            return expectedCategories.All(c => actual.Contains(c.Id.Value));
        }

        private static bool BindingContainsCategory(ElementBinding binding, Category expectedCategory)
        {
            if (expectedCategory == null)
            {
                return false;
            }

            foreach (Category category in binding.Categories.Cast<Category>())
            {
                if (category?.Id?.Value == expectedCategory.Id.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private class ParameterExpectation
        {
            public ParameterExpectation(string displayName, string expectedBindingType, bool projectInfo, params string[] lookupNames)
            {
                DisplayName = displayName;
                ExpectedBindingType = expectedBindingType;
                ProjectInfo = projectInfo;
                LookupNames = lookupNames ?? Array.Empty<string>();
            }

            public string DisplayName { get; }
            public string ExpectedBindingType { get; }
            public bool ProjectInfo { get; }
            public IList<string> LookupNames { get; }
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
