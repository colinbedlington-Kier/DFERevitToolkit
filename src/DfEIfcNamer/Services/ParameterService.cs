using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ParameterService
    {
        private const string SharedParameterGroupName = "DfE IFC Namer";
        private const string SharedParameterFileName = "DfE_IfcNamer_SharedParameters.txt";

        private static readonly ParameterSpec[] InstanceParameters =
        {
            ParameterSpec.Instance("IfcName", "IfcName", "IFCName"),
            ParameterSpec.Instance("IfcDescription", "IfcDescription"),
            ParameterSpec.Instance("DfE_IFCPredefinedType", "DfE_IFCPredefinedType"),
            ParameterSpec.Instance("DfE_UserDefinedPredefinedTypeValue", "DfE_UserDefinedPredefinedTypeValue"),
            ParameterSpec.Instance("DfE_IFCEntity", "DfE_IFCEntity")
        };

        private static readonly ParameterSpec[] TypeParameters =
        {
            ParameterSpec.Type("IfcName[Type]", "IfcName[Type]", "IFCName[Type]", "IFCName [Type]"),
            ParameterSpec.Type("IfcDescription[Type]", "IfcDescription[Type]"),
            ParameterSpec.Type("Classification", "Classification"),
            ParameterSpec.Type("Classification(2)", "Classification(2)"),
            ParameterSpec.Type("Classification(3)", "Classification(3)"),
            ParameterSpec.Type("Classification(4)", "Classification(4)"),
            ParameterSpec.Type("Classification(5)", "Classification(5)"),
            ParameterSpec.Type("Classification(6)", "Classification(6)"),
            ParameterSpec.Type("Classification(7)", "Classification(7)"),
            ParameterSpec.Type("Classification(8)", "Classification(8)"),
            ParameterSpec.Type("Classification(9)", "Classification(9)")
        };

        private static readonly ParameterSpec[] ProjectInfoParameters =
        {
            ParameterSpec.ProjectInfo("DfE_ProjectInfoJson", "DfE_ProjectInfoJson"),
            ParameterSpec.ProjectInfo("DfE_NamingCounters", "DfE_NamingCounters")
        };

        public string ResolveAddinFolder()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        }

        public string ResolveSharedParameterFilePath()
        {
            var addinFolder = ResolveAddinFolder();
            var resourcesPath = Path.Combine(addinFolder, "Resources", SharedParameterFileName);
            if (File.Exists(resourcesPath))
            {
                return resourcesPath;
            }

            return Path.Combine(addinFolder, SharedParameterFileName);
        }

        public ParameterBindingSummary EnsureIfcNameParameters(Document doc, IList<Category> categories)
        {
            var summary = new ParameterBindingSummary();
            var sharedPath = ResolveSharedParameterFilePath();
            summary.SharedParameterFilePath = sharedPath;

            if (!EnsureSharedParameterFileConfigured(doc.Application, sharedPath))
            {
                summary.ErrorMessage = "Shared parameter file missing at: " + sharedPath;
                InitializeUnresolvedResults(summary);
                return summary;
            }

            var file = doc.Application.OpenSharedParameterFile();
            if (file == null)
            {
                summary.ErrorMessage = "OpenSharedParameterFile() returned null. Expected: " + sharedPath;
                InitializeUnresolvedResults(summary);
                return summary;
            }

            var group = file.Groups.get_Item(SharedParameterGroupName);
            if (group == null)
            {
                summary.ErrorMessage = "Shared parameter group not found: " + SharedParameterGroupName;
                InitializeUnresolvedResults(summary);
                return summary;
            }

            var selectedIds = categories == null
                ? null
                : new HashSet<long>(categories.Where(c => c != null).Select(c => c.Id.Value));
            var allModelCategories = doc.Settings.Categories.Cast<Category>().ToList();
            var modelCategories = BuildValidModelCategoryList(allModelCategories, selectedIds, summary);

            var projectInfoCategorySet = doc.Application.Create.NewCategorySet();
            var projectInfoCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);
            if (projectInfoCategory != null)
            {
                projectInfoCategorySet.Insert(projectInfoCategory);
            }

            var modelCategorySet = BuildCategorySet(doc, modelCategories, summary);

            using (var tg = new TransactionGroup(doc, "DfE IFC Bootstrap Parameters"))
            {
                tg.Start();
                BindParameterSet(doc, group, InstanceParameters, modelCategorySet, GroupTypeId.Ifc, summary);
                BindParameterSet(doc, group, TypeParameters, modelCategorySet, GroupTypeId.Ifc, summary);
                BindParameterSet(doc, group, ProjectInfoParameters, projectInfoCategorySet, GroupTypeId.Data, summary);
                tg.Assimilate();
            }

            VerifyBindings(doc, modelCategories, projectInfoCategory, summary);
            PopulateSummaryCounts(summary);
            return summary;
        }

        private static void InitializeUnresolvedResults(ParameterBindingSummary summary)
        {
            foreach (var spec in AllSpecs())
            {
                summary.ParameterResults.Add(new ParameterBindingResult
                {
                    Name = spec.DisplayName,
                    ExpectedBindingType = spec.ExpectedBindingType,
                    FoundInSharedParameterFile = false,
                    InsertSucceeded = false,
                    ReInsertSucceeded = false,
                    FinalBoundState = false,
                    BindingAction = "None",
                    Notes = "Shared parameter file/group unavailable."
                });
            }

            PopulateSummaryCounts(summary);
        }

        private static IList<Category> BuildValidModelCategoryList(IEnumerable<Category> categories, HashSet<long> selectedIds, ParameterBindingSummary summary)
        {
            var result = new List<Category>();
            foreach (var category in categories.Where(c => c != null).OrderBy(c => c.Name))
            {
                if (selectedIds != null && selectedIds.Count > 0 && !selectedIds.Contains(category.Id.Value))
                {
                    continue;
                }

                if (!category.AllowsBoundParameters || category.IsTagCategory || category.CategoryType == CategoryType.Internal)
                {
                    summary.SkippedUnsupportedCategoriesCount++;
                    continue;
                }

                result.Add(category);
                summary.IncludedCategoryNames.Add(category.Name);
                summary.IncludedCategoriesCount++;
            }

            return result;
        }

        private static CategorySet BuildCategorySet(Document doc, IEnumerable<Category> categories, ParameterBindingSummary summary)
        {
            var set = doc.Application.Create.NewCategorySet();
            foreach (var category in categories)
            {
                try
                {
                    set.Insert(category);
                }
                catch (Exception ex)
                {
                    summary.SkippedUnsupportedCategoriesCount++;
                    summary.ErrorMessage = AppendError(summary.ErrorMessage, $"Category insert skipped for '{category.Name}': {ex.Message}");
                }
            }

            return set;
        }

        private static void BindParameterSet(Document doc, DefinitionGroup group, IEnumerable<ParameterSpec> specs, CategorySet categorySet, ForgeTypeId groupTypeId, ParameterBindingSummary summary)
        {
            foreach (var spec in specs)
            {
                var result = new ParameterBindingResult
                {
                    Name = spec.DisplayName,
                    ExpectedBindingType = spec.ExpectedBindingType,
                    BindingAction = "None"
                };

                try
                {
                    var definition = ResolveDefinition(group, spec, out var resolvedName);
                    result.FoundInSharedParameterFile = definition != null;
                    if (definition == null)
                    {
                        result.Notes = "Missing definition in shared parameter file: " + spec.DisplayName;
                        summary.ParameterResults.Add(result);
                        continue;
                    }

                    var binding = spec.ExpectedBindingType == "type"
                        ? (Binding)doc.Application.Create.NewTypeBinding(categorySet)
                        : doc.Application.Create.NewInstanceBinding(categorySet);

                    result.InsertSucceeded = doc.ParameterBindings.Insert(definition, binding, groupTypeId);
                    if (result.InsertSucceeded)
                    {
                        result.BindingAction = "Insert";
                    }
                    else
                    {
                        result.ReInsertSucceeded = doc.ParameterBindings.ReInsert(definition, binding, groupTypeId);
                        result.BindingAction = result.ReInsertSucceeded ? "ReInsert" : "Insert/ReInsert failed";
                    }

                    if (!string.Equals(resolvedName, spec.DisplayName, StringComparison.Ordinal))
                    {
                        result.Notes = $"Resolved shared parameter definition '{resolvedName}' for requested '{spec.DisplayName}'.";
                    }
                }
                catch (Exception ex)
                {
                    result.Notes = ex.Message;
                    result.BindingAction = "Exception";
                    summary.FailedBindingInsertCount++;
                }

                summary.ParameterResults.Add(result);
            }
        }

        private static ExternalDefinition ResolveDefinition(DefinitionGroup group, ParameterSpec spec, out string resolvedName)
        {
            foreach (var candidate in spec.LookupNames)
            {
                var definition = group.Definitions.get_Item(candidate) as ExternalDefinition;
                if (definition != null)
                {
                    resolvedName = candidate;
                    return definition;
                }
            }

            resolvedName = null;
            return null;
        }

        private static void VerifyBindings(Document doc, IList<Category> modelCategories, Category projectInfoCategory, ParameterBindingSummary summary)
        {
            var bindingMap = GetBindingMap(doc);
            foreach (var result in summary.ParameterResults)
            {
                var spec = AllSpecs().First(s => s.DisplayName == result.Name);
                string definitionName;
                ElementBinding binding;
                var bound = ResolveBinding(bindingMap, spec, out definitionName, out binding);
                if (!bound || binding == null)
                {
                    result.FinalBoundState = false;
                    result.Notes = AppendNote(result.Notes, "Definition not bound in document.");
                    continue;
                }

                var kindOk = spec.ExpectedBindingType == "type" ? binding is TypeBinding : binding is InstanceBinding;
                var categoriesOk = spec.ExpectedBindingType == "project info"
                    ? BindingContainsCategory(binding, projectInfoCategory)
                    : BindingCoversCategories(binding, modelCategories);

                result.FinalBoundState = kindOk && categoriesOk;
                if (!kindOk)
                {
                    result.Notes = AppendNote(result.Notes, "Binding kind mismatch for definition '" + definitionName + "'.");
                }
            }

            return false;
        }

        private static bool EnsureSharedParameterFileConfigured(Autodesk.Revit.ApplicationServices.Application app, string sharedPath)
        {
            if (!File.Exists(sharedPath))
            {
                return false;
            }

            app.SharedParametersFilename = sharedPath;
            return true;
        }

        private static string AppendError(string existing, string next)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return next;
            }

            return existing + " | " + next;
        }

                if (!categoriesOk)
                {
                    result.Notes = AppendNote(result.Notes, "Binding categories do not match expected scope.");
                }
            }

            return existing + " " + note;
        }

        private static IEnumerable<ParameterSpec> AllSpecs()
        {
            return InstanceParameters.Concat(TypeParameters).Concat(ProjectInfoParameters);
        }

        private static void PopulateSummaryCounts(ParameterBindingSummary summary)
        {
            summary.ParametersRequestedCount = summary.ParameterResults.Count;
            summary.ParametersFoundInSharedFileCount = summary.ParameterResults.Count(x => x.FoundInSharedParameterFile);
            summary.InsertSucceededCount = summary.ParameterResults.Count(x => x.InsertSucceeded);
            summary.ReInsertSucceededCount = summary.ParameterResults.Count(x => x.ReInsertSucceeded);
            summary.VerifiedBoundCount = summary.ParameterResults.Count(x => x.FinalBoundState);
            summary.VerificationFailedCount = summary.ParameterResults.Count(x => !x.FinalBoundState);
        }

        public class ParameterBindingSummary
        {
            public string SharedParameterFilePath { get; set; }
            public int IncludedCategoriesCount { get; set; }
            public int SkippedUnsupportedCategoriesCount { get; set; }
            public int FailedBindingInsertCount { get; set; }
            public int ParametersRequestedCount { get; set; }
            public int ParametersFoundInSharedFileCount { get; set; }
            public int InsertSucceededCount { get; set; }
            public int ReInsertSucceededCount { get; set; }
            public int VerifiedBoundCount { get; set; }
            public int VerificationFailedCount { get; set; }
            public string ErrorMessage { get; set; }
            public IList<string> IncludedCategoryNames { get; } = new List<string>();
            public IList<ParameterBindingResult> ParameterResults { get; } = new List<ParameterBindingResult>();
        }

        private class ParameterSpec
        {
            private ParameterSpec(string displayName, string expectedBindingType, params string[] lookupNames)
            {
                DisplayName = displayName;
                ExpectedBindingType = expectedBindingType;
                LookupNames = lookupNames;
            }

            public string DisplayName { get; }
            public string ExpectedBindingType { get; }
            public IList<string> LookupNames { get; }

            public static ParameterSpec Instance(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "instance", lookupNames);
            public static ParameterSpec Type(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "type", lookupNames);
            public static ParameterSpec ProjectInfo(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "project info", lookupNames);
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

        private static bool ResolveBinding(Dictionary<string, ElementBinding> map, ParameterSpec spec, out string definitionName, out ElementBinding binding)
        {
            foreach (var candidate in spec.LookupNames)
            {
                ElementBinding resolvedBinding;
                if (map.TryGetValue(candidate, out resolvedBinding))
                {
                    definitionName = candidate;
                    binding = resolvedBinding;
                    return true;
                }
            }

            binding = null;
            definitionName = null;
            return false;
        }

        private static bool BindingCoversCategories(ElementBinding binding, IList<Category> expectedCategories)
        {
            var actual = new HashSet<long>(binding.Categories.Cast<Category>().Where(c => c != null).Select(c => c.Id.Value));
            return expectedCategories.All(c => actual.Contains(c.Id.Value));
        }

        private static bool BindingContainsCategory(ElementBinding binding, Category expectedCategory)
        {
            if (expectedCategory == null)
            {
                return false;
            }

            var expectedId = expectedCategory.Id.Value;
            foreach (Category category in binding.Categories.Cast<Category>())
            {
                if (category == null)
                {
                    continue;
                }

                if (category.Id.Value == expectedId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnsureSharedParameterFileConfigured(Autodesk.Revit.ApplicationServices.Application app, string sharedPath)
        {
            if (!File.Exists(sharedPath))
            {
                return false;
            }

            app.SharedParametersFilename = sharedPath;
            return true;
        }

        private static string AppendError(string existing, string next)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return next;
            }

            return existing + " | " + next;
        }

        private static string AppendNote(string existing, string note)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return note;
            }

            return existing + " " + note;
        }

        private static IEnumerable<ParameterSpec> AllSpecs()
        {
            return InstanceParameters.Concat(TypeParameters).Concat(ProjectInfoParameters);
        }

        private static void PopulateSummaryCounts(ParameterBindingSummary summary)
        {
            summary.ParametersRequestedCount = summary.ParameterResults.Count;
            summary.ParametersFoundInSharedFileCount = summary.ParameterResults.Count(x => x.FoundInSharedParameterFile);
            summary.InsertSucceededCount = summary.ParameterResults.Count(x => x.InsertSucceeded);
            summary.ReInsertSucceededCount = summary.ParameterResults.Count(x => x.ReInsertSucceeded);
            summary.VerifiedBoundCount = summary.ParameterResults.Count(x => x.FinalBoundState);
            summary.VerificationFailedCount = summary.ParameterResults.Count(x => !x.FinalBoundState);
        }

        public class ParameterBindingSummary
        {
            public string SharedParameterFilePath { get; set; }
            public int IncludedCategoriesCount { get; set; }
            public int SkippedUnsupportedCategoriesCount { get; set; }
            public int FailedBindingInsertCount { get; set; }
            public int ParametersRequestedCount { get; set; }
            public int ParametersFoundInSharedFileCount { get; set; }
            public int InsertSucceededCount { get; set; }
            public int ReInsertSucceededCount { get; set; }
            public int VerifiedBoundCount { get; set; }
            public int VerificationFailedCount { get; set; }
            public string ErrorMessage { get; set; }
            public IList<string> IncludedCategoryNames { get; } = new List<string>();
            public IList<ParameterBindingResult> ParameterResults { get; } = new List<ParameterBindingResult>();
        }

        private class ParameterSpec
        {
            private ParameterSpec(string displayName, string expectedBindingType, params string[] lookupNames)
            {
                DisplayName = displayName;
                ExpectedBindingType = expectedBindingType;
                LookupNames = lookupNames;
            }

            public string DisplayName { get; }
            public string ExpectedBindingType { get; }
            public IList<string> LookupNames { get; }

            public static ParameterSpec Instance(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "instance", lookupNames);
            public static ParameterSpec Type(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "type", lookupNames);
            public static ParameterSpec ProjectInfo(string displayName, params string[] lookupNames) => new ParameterSpec(displayName, "project info", lookupNames);
        }
    }
}
