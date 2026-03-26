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
        private const string SharedParameterFileName = "DfE_IfcNamer_SharedParameters.txt";

        private static readonly ParameterSpec[] InstanceParameters =
        {
            ParameterSpec.Instance("IFCName", "IFCName", "IfcName"),
            ParameterSpec.Instance("IfcDescription", "IfcDescription"),
            ParameterSpec.Instance("DfE_IFCPredefinedType", "DfE_IFCPredefinedType"),
            ParameterSpec.Instance("DfE_UserDefinedPredefinedTypeValue", "DfE_UserDefinedPredefinedTypeValue"),
            ParameterSpec.Instance("DfE_IFCEntity", "DfE_IFCEntity")
        };

        private static readonly ParameterSpec[] TypeParameters =
        {
            ParameterSpec.Type("IFCName [Type]", "IFCName [Type]", "IFCName[Type]", "IfcName[Type]"),
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

        public ParameterBindingSummary EnsureIfcNameParameters(Document doc, IList<Category> categories, bool transactionAlreadyOpen = false)
        {
            return ExecuteIfcNameParameterBinding(doc, categories, diagnosticOnly: false, rollbackAtEnd: false, transactionAlreadyOpen: transactionAlreadyOpen);
        }

        public ParameterBindingSummary DiagnoseIfcNameParameters(
            Document doc,
            IList<Category> categories,
            bool rollbackAtEnd = true,
            bool transactionAlreadyOpen = false)
        {
            return ExecuteIfcNameParameterBinding(
                doc,
                categories,
                diagnosticOnly: true,
                rollbackAtEnd: rollbackAtEnd,
                transactionAlreadyOpen: transactionAlreadyOpen);
        }

        private ParameterBindingSummary ExecuteIfcNameParameterBinding(
            Document doc,
            IList<Category> categories,
            bool diagnosticOnly,
            bool rollbackAtEnd,
            bool transactionAlreadyOpen)
        {
            var summary = new ParameterBindingSummary();
            summary.DiagnosticOnly = diagnosticOnly;
            summary.DiagnosticRollbackUsed = diagnosticOnly && rollbackAtEnd;
            summary.DocumentModifiableOnEntry = doc.IsModifiable;
            summary.CallerHadActiveTransaction = transactionAlreadyOpen;
            try
            {
                var sharedPath = ResolveSharedParameterFilePath();
                summary.SharedParameterFilePath = sharedPath;

                if (!EnsureSharedParameterFileConfigured(doc.Application, sharedPath, out var validationError))
                {
                    summary.ErrorMessage = validationError;
                    InitializeUnresolvedResults(summary, validationError);
                    return summary;
                }

                DefinitionFile file;
                try
                {
                    file = doc.Application.OpenSharedParameterFile();
                }
                catch (Exception ex)
                {
                    summary.ErrorMessage = $"OpenSharedParameterFile failed for '{sharedPath}': {ex}";
                    InitializeUnresolvedResults(summary, ex.GetType().Name + " during OpenSharedParameterFile");
                    return summary;
                }

                if (file == null)
                {
                    summary.ErrorMessage = "OpenSharedParameterFile() returned null. Expected: " + sharedPath;
                    InitializeUnresolvedResults(summary, "OpenSharedParameterFile returned null");
                    return summary;
                }

                var groups = file.Groups.Cast<DefinitionGroup>().ToList();
                if (groups.Count == 0)
                {
                    summary.ErrorMessage = "Parameter not found in any shared parameter group";
                    InitializeUnresolvedResults(summary, "Parameter not found in any shared parameter group");
                    return summary;
                }
                var definitionLookup = BuildDefinitionLookup(groups, summary);

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

                RunBindingInTransaction(doc, summary, () =>
                {
                    BindParameterSet(doc, definitionLookup, InstanceParameters, modelCategorySet, GroupTypeId.Ifc, summary, diagnosticOnly, rollbackAtEnd);
                    BindParameterSet(doc, definitionLookup, TypeParameters, modelCategorySet, GroupTypeId.Ifc, summary, diagnosticOnly, rollbackAtEnd);
                    BindParameterSet(doc, definitionLookup, ProjectInfoParameters, projectInfoCategorySet, GroupTypeId.Data, summary, diagnosticOnly, rollbackAtEnd);
                }, rollbackAtEnd, transactionAlreadyOpen);

                if (summary.ParameterResults.Count == 0)
                {
                    InitializeUnresolvedResults(summary, summary.ErrorMessage ?? "Binding operation did not execute.");
                    return summary;
                }

                if (diagnosticOnly && rollbackAtEnd)
                {
                    foreach (var result in summary.ParameterResults)
                    {
                        result.PersistedToModel = false;
                        result.VerificationStatus = "n/a - diagnostic rollback";
                        result.FinalBoundState = result.InsertSucceeded || result.ReInsertSucceeded;
                        result.BindingAction = result.InsertSucceeded
                            ? "Insert"
                            : result.ReInsertSucceeded
                                ? "ReInsert"
                                : result.BindingAction;
                    }
                }
                else
                {
                    VerifyBindings(doc, modelCategories, projectInfoCategory, summary);
                }

                PopulateSummaryCounts(summary);
            }
            catch (Exception ex)
            {
                summary.ErrorMessage = NormalizeLegacyErrorMessage(ex.Message);
                if (summary.ParameterResults.Count == 0)
                {
                    InitializeUnresolvedResults(summary, summary.ErrorMessage);
                }
            }

            return summary;
        }

        private static void InitializeUnresolvedResults(ParameterBindingSummary summary, string reason)
        {
            foreach (var spec in AllSpecs())
            {
                summary.ParameterResults.Add(new ParameterBindingResult
                {
                    Name = spec.DisplayName,
                    RequestedName = spec.DisplayName,
                    BindingType = spec.ExpectedBindingType,
                    ExpectedBindingType = spec.ExpectedBindingType,
                    FoundInSharedParameterFile = false,
                    InsertSucceeded = false,
                    ReInsertSucceeded = false,
                    PersistedToModel = false,
                    VerificationStatus = "failed",
                    FinalBoundState = false,
                    BindingAction = "None",
                    Notes = string.IsNullOrWhiteSpace(reason) ? "Binding prerequisites were not met." : reason
                });
            }

            PopulateSummaryCounts(summary);
        }

        private static IList<Category> BuildValidModelCategoryList(
            IEnumerable<Category> categories,
            HashSet<long> selectedIds,
            ParameterBindingSummary summary)
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
                    summary.ErrorMessage = AppendError(summary.ErrorMessage, $"Category insert skipped for '{category.Name}': {NormalizeLegacyErrorMessage(ex.Message)}");
                }
            }

            return set;
        }

        private static void BindParameterSet(
            Document doc,
            IReadOnlyDictionary<string, DefinitionEntry> definitionLookup,
            IEnumerable<ParameterSpec> specs,
            CategorySet categorySet,
            ForgeTypeId groupTypeId,
            ParameterBindingSummary summary,
            bool diagnosticOnly,
            bool rollbackAtEnd)
        {
            foreach (var spec in specs)
            {
                var result = new ParameterBindingResult
                {
                    Name = spec.DisplayName,
                    RequestedName = spec.DisplayName,
                    ExpectedBindingType = spec.ExpectedBindingType,
                    BindingType = spec.ExpectedBindingType,
                    DiagnosticRollbackUsed = diagnosticOnly && rollbackAtEnd,
                    BindingAction = "None"
                };

                try
                {
                    var definition = ResolveDefinition(definitionLookup, spec, out var resolvedName, out var resolvedGroup);
                    result.FoundInSharedParameterFile = definition != null;
                    result.ResolvedDefinitionName = resolvedName;
                    result.ResolvedGroup = resolvedGroup;
                    result.Notes = $"Requested='{spec.DisplayName}', BindingType='{spec.ExpectedBindingType}'.";

                    if (definition == null)
                    {
                        result.Notes = "Parameter not found in any shared parameter group: " + spec.DisplayName;
                        summary.ParameterResults.Add(result);
                        continue;
                    }

                    var binding = spec.ExpectedBindingType == "type"
                        ? (Binding)doc.Application.Create.NewTypeBinding(categorySet)
                        : doc.Application.Create.NewInstanceBinding(categorySet);

                    var existingBindingPresent = ResolveBinding(GetBindingMap(doc), spec, out var existingDefinition, out var existingBinding);
                    result.ExistingBindingPresent = existingBindingPresent;
                    result.Notes = AppendNote(result.Notes, $"ResolvedName='{resolvedName}', ResolvedGroup='{resolvedGroup}', ExistingBindingPresent={existingBindingPresent}.");
                    if (existingBindingPresent)
                    {
                        var existingBindingKind = existingBinding is TypeBinding ? "type" : existingBinding is InstanceBinding ? "instance" : "unknown";
                        var existingCategoryCount = existingBinding.Categories?.Size ?? 0;
                        result.Notes = AppendNote(result.Notes, $"ExistingBindingKind={existingBindingKind}, ExistingBindingCategoryCount={existingCategoryCount}, ExistingDefinitionName='{existingDefinition}'.");
                    }

                    result.InsertAttempted = true;
                    result.InsertSucceeded = doc.ParameterBindings.Insert(definition, binding, groupTypeId);
                    result.Notes = AppendNote(result.Notes, $"InsertResult={result.InsertSucceeded}.");
                    if (result.InsertSucceeded)
                    {
                        result.BindingAction = "Insert";
                    }
                    else
                    {
                        result.Notes = AppendNote(result.Notes, "Insert returned false; existing binding present.");
                        result.ReInsertAttempted = true;
                        result.ReInsertSucceeded = doc.ParameterBindings.ReInsert(definition, binding, groupTypeId);
                        result.Notes = AppendNote(result.Notes, $"ReInsertResult={result.ReInsertSucceeded}.");
                        result.BindingAction = result.ReInsertSucceeded ? "ReInsert" : "Insert/ReInsert failed";
                        if (!result.ReInsertSucceeded)
                        {
                            result.Notes = AppendNote(result.Notes, "Definition resolved successfully but binding failed.");
                        }
                    }

                    if (!string.Equals(resolvedName, spec.DisplayName, StringComparison.Ordinal))
                    {
                        result.Notes = $"Resolved shared parameter definition '{resolvedName}' for requested '{spec.DisplayName}'.";
                    }

                    result.Notes = AppendNote(result.Notes, $"Definition located in shared parameter group '{resolvedGroup}'.");
                }
                catch (Exception ex)
                {
                    var operation = result.InsertSucceeded || result.ReInsertSucceeded
                        ? "ReInsert"
                        : "Insert";
                    result.Notes = $"{ex.GetType().Name} during {operation}: {NormalizeLegacyErrorMessage(ex.Message)}";
                    result.BindingAction = "Exception";
                    summary.FailedBindingInsertCount++;
                }

                result.PersistedToModel = !(diagnosticOnly && rollbackAtEnd) && (result.InsertSucceeded || result.ReInsertSucceeded);
                if (!diagnosticOnly || !rollbackAtEnd)
                {
                    result.VerificationStatus = "pending";
                }

                summary.ParameterResults.Add(result);
            }
        }

        private static void RunBindingInTransaction(
            Document doc,
            ParameterBindingSummary summary,
            Action operation,
            bool rollbackAtEnd,
            bool transactionAlreadyOpen)
        {
            summary.DocumentModifiableOnEntry = doc.IsModifiable;
            summary.CallerHadActiveTransaction = transactionAlreadyOpen;

            if (transactionAlreadyOpen)
            {
                if (!doc.IsModifiable)
                {
                    summary.AbortedDueToNestedTransactionProtection = true;
                    summary.ErrorMessage = AppendError(
                        summary.ErrorMessage,
                        "Binding expected an active caller transaction, but document is not modifiable.");
                    return;
                }

                summary.MethodStartedTransaction = false;
                summary.TransactionCommitted = false;
                operation();
                return;
            }

            if (doc.IsModifiable)
            {
                summary.AbortedDueToNestedTransactionProtection = true;
                summary.ErrorMessage = AppendError(summary.ErrorMessage, "Cannot bind parameters inside an active Revit transaction.");
                return;
            }

            using (var tx = new Transaction(doc, "Bind DfE Parameters"))
            {
                summary.MethodStartedTransaction = true;
                tx.Start();
                try
                {
                    operation();
                    if (rollbackAtEnd)
                    {
                        tx.RollBack();
                        summary.TransactionCommitted = false;
                    }
                    else
                    {
                        tx.Commit();
                        summary.TransactionCommitted = true;
                    }
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    summary.ErrorMessage = AppendError(summary.ErrorMessage, ex.GetType().Name + " during binding transaction");
                    throw;
                }
            }
        }

        private static Dictionary<string, DefinitionEntry> BuildDefinitionLookup(
            IEnumerable<DefinitionGroup> groups,
            ParameterBindingSummary summary)
        {
            var lookup = new Dictionary<string, DefinitionEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var definitions = group.Definitions.Cast<Definition>().ToList();
                summary.SharedParameterGroupNames.Add(group.Name);
                summary.SharedParameterDefinitionCountsByGroup[group.Name] = definitions.Count;
                foreach (var definition in definitions.OfType<ExternalDefinition>())
                {
                    if (!lookup.ContainsKey(definition.Name))
                    {
                        lookup[definition.Name] = new DefinitionEntry(definition, group.Name);
                    }

                    var guidKey = definition.GUID.ToString("D");
                    if (!lookup.ContainsKey(guidKey))
                    {
                        lookup[guidKey] = new DefinitionEntry(definition, group.Name);
                    }
                }
            }

            return lookup;
        }

        private static ExternalDefinition ResolveDefinition(
            IReadOnlyDictionary<string, DefinitionEntry> definitionLookup,
            ParameterSpec spec,
            out string resolvedName,
            out string resolvedGroup)
        {
            if (!string.IsNullOrWhiteSpace(spec.Guid) &&
                definitionLookup.TryGetValue(spec.Guid, out var guidMatch))
            {
                resolvedName = guidMatch.Definition.Name;
                resolvedGroup = guidMatch.GroupName;
                return guidMatch.Definition;
            }

            foreach (var candidate in spec.LookupNames)
            {
                if (definitionLookup.TryGetValue(candidate, out var nameMatch))
                {
                    resolvedName = nameMatch.Definition.Name;
                    resolvedGroup = nameMatch.GroupName;
                    return nameMatch.Definition;
                }
            }

            resolvedName = null;
            resolvedGroup = null;
            return null;
        }

        private static void VerifyBindings(
            Document doc,
            IList<Category> modelCategories,
            Category projectInfoCategory,
            ParameterBindingSummary summary)
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
                    result.PersistedToModel = false;
                    result.VerificationStatus = "failed";
                    result.Notes = AppendNote(result.Notes, "Definition not bound in document.");
                    continue;
                }

                var kindOk = spec.ExpectedBindingType == "type"
                    ? binding is TypeBinding
                    : binding is InstanceBinding;

                bool categoriesOk;
                if (spec.ExpectedBindingType == "project info")
                {
                    categoriesOk = BindingContainsCategory(binding, projectInfoCategory);
                }
                else
                {
                    categoriesOk = BindingCoversCategories(binding, modelCategories);
                }

                result.FinalBoundState = kindOk && categoriesOk;
                result.PersistedToModel = result.FinalBoundState;
                result.VerificationStatus = result.FinalBoundState ? "verified" : "failed";

                if (!kindOk)
                {
                    result.Notes = AppendNote(result.Notes, "Binding kind mismatch for definition '" + definitionName + "'.");
                }

                if (!categoriesOk)
                {
                    result.Notes = AppendNote(result.Notes, "Binding categories do not match expected scope.");
                }
            }
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

        private static bool ResolveBinding(
            Dictionary<string, ElementBinding> map,
            ParameterSpec spec,
            out string definitionName,
            out ElementBinding binding)
        {
            foreach (var candidate in spec.LookupNames)
            {
                if (map.TryGetValue(candidate, out binding))
                {
                    definitionName = candidate;
                    return true;
                }
            }

            binding = null;
            definitionName = null;
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

        internal static bool EnsureSharedParameterFileConfigured(
            Autodesk.Revit.ApplicationServices.Application app,
            string sharedPath,
            out string error)
        {
            if (!File.Exists(sharedPath))
            {
                error = "Shared parameter file missing at: " + sharedPath;
                return false;
            }

            try
            {
                using (var stream = File.Open(sharedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length == 0)
                    {
                        error = "Shared parameter file is empty: " + sharedPath;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Shared parameter file is not readable at '{sharedPath}': {ex.Message}";
                return false;
            }

            error = null;
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

        private static string NormalizeLegacyErrorMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Shared parameter assignment failed.";
            }

            var compact = new string(message.Where(char.IsLetterOrDigit).ToArray());
            return compact.IndexOf("readparamdatabase", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Shared parameter assignment failed while binding parameters to the model."
                : message;
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
            summary.VerifiedBoundCount = summary.ParameterResults.Count(x => string.Equals(x.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase));
            summary.VerificationFailedCount = summary.ParameterResults.Count(x => string.Equals(x.VerificationStatus, "failed", StringComparison.OrdinalIgnoreCase));
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
            public bool DiagnosticOnly { get; set; }
            public bool DiagnosticRollbackUsed { get; set; }
            public bool DocumentModifiableOnEntry { get; set; }
            public bool CallerHadActiveTransaction { get; set; }
            public bool MethodStartedTransaction { get; set; }
            public bool TransactionCommitted { get; set; }
            public bool AbortedDueToNestedTransactionProtection { get; set; }
            public string ErrorMessage { get; set; }
            public IList<string> IncludedCategoryNames { get; } = new List<string>();
            public IList<string> SharedParameterGroupNames { get; } = new List<string>();
            public IDictionary<string, int> SharedParameterDefinitionCountsByGroup { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public IList<ParameterBindingResult> ParameterResults { get; } = new List<ParameterBindingResult>();
        }

        private sealed class DefinitionEntry
        {
            public DefinitionEntry(ExternalDefinition definition, string groupName)
            {
                Definition = definition;
                GroupName = groupName;
            }

            public ExternalDefinition Definition { get; }
            public string GroupName { get; }
        }

        private class ParameterSpec
        {
            private ParameterSpec(string displayName, string expectedBindingType, string guid, params string[] lookupNames)
            {
                DisplayName = displayName;
                ExpectedBindingType = expectedBindingType;
                Guid = guid;
                LookupNames = lookupNames;
            }

            public string DisplayName { get; }
            public string ExpectedBindingType { get; }
            public string Guid { get; }
            public IList<string> LookupNames { get; }

            public static ParameterSpec Instance(string displayName, params string[] lookupNames)
                => new ParameterSpec(displayName, "instance", null, lookupNames);

            public static ParameterSpec Type(string displayName, params string[] lookupNames)
                => new ParameterSpec(displayName, "type", null, lookupNames);

            public static ParameterSpec ProjectInfo(string displayName, params string[] lookupNames)
                => new ParameterSpec(displayName, "project info", null, lookupNames);
        }
    }
}
