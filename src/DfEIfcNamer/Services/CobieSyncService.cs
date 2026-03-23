using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class CobieSyncService
    {
        private static readonly ParameterExpectation[] ExpectedParameters =
        {
            new ParameterExpectation("IFCName", "instance", false, "IFCName", "IfcName"),
            new ParameterExpectation("IfcDescription", "instance", false, "IfcDescription"),
            new ParameterExpectation("DfE_IFCPredefinedType", "instance", false, "DfE_IFCPredefinedType"),
            new ParameterExpectation("DfE_UserDefinedPredefinedTypeValue", "instance", false, "DfE_UserDefinedPredefinedTypeValue"),
            new ParameterExpectation("DfE_IFCEntity", "instance", false, "DfE_IFCEntity"),
            new ParameterExpectation("IFCName [Type]", "type", false, "IFCName [Type]", "IFCName[Type]", "IfcName[Type]"),
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
        private readonly DiagnosticsCollectorService _diagnostics;
        private readonly SharedParameterFileInspector _fileInspector;

        public CobieSyncService(
            ParameterService parameterService,
            ResourceJsonService resourceJsonService,
            DiagnosticsCollectorService diagnostics,
            SharedParameterFileInspector fileInspector)
        {
            _parameterService = parameterService;
            _resourceJsonService = resourceJsonService;
            _diagnostics = diagnostics;
            _fileInspector = fileInspector;
        }

        public SetupStatus CheckSetup(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var status = BuildStatusSkeleton();
            _diagnostics.AddInfo("CheckSetup", "Setup diagnostics started.");

            try
            {
                var categories = GetModelCategories(doc, selectedCategoryIds, out var skippedUnsupported);
                status.IncludedCategoriesCount = categories.Count;
                status.SkippedUnsupportedCategoriesCount = skippedUnsupported;
                status.IncludedCategoryNames = categories.Select(c => c.Name).ToList();

                status.SharedParameterFileFound = System.IO.File.Exists(status.SharedParameterFilePath);
                status.EntityMappingFileExists = System.IO.File.Exists(status.EntityMappingJsonPath);
                status.ClassificationSlotsFileExists = System.IO.File.Exists(status.ClassificationSlotsJsonPath);
                var entityLibrary = SafeLoadEntityLibrary(out var entityError);
                status.EntityMappingLoaded = string.IsNullOrWhiteSpace(entityError) && entityLibrary != null;
                status.ClassificationSlotsLoaded = TryLoad(() => _resourceJsonService.LoadClassificationSlots(), out var classificationError);
                status.IfcClassesLoadedCount = entityLibrary?.Count ?? 0;
                status.IfcPredefinedTypesLoadedCount = entityLibrary?.Sum(x => x.PredefinedTypes?.Count ?? 0) ?? 0;

                status.InstanceParameterBound = IsParameterBound(doc, new[] { "IFCName", "IfcName" }, false, categories);
                status.TypeParameterBound = IsParameterBound(doc, new[] { "IFCName [Type]", "IFCName[Type]", "IfcName[Type]" }, true, categories);
                var sharedFileMatches = ResolveExpectedDefinitionsInSharedFile(doc);
                status.ParameterResults = BuildVerificationResults(doc, categories, sharedFileMatches).ToList();
                status.ParametersRequestedCount = status.ParameterResults.Count;
                status.ParametersFoundInSharedFileCount = status.ParameterResults.Count(x => x.FoundInSharedParameterFile);
                status.VerifiedBoundCount = status.ParameterResults.Count(x => x.FinalBoundState);
                status.VerificationFailedCount = status.ParameterResults.Count(x => !x.FinalBoundState);
                status.InvalidIfcMetadataNotes = ValidateIfcMetadataAgainstLibrary(doc, entityLibrary);
                status.InvalidIfcMetadataCount = status.InvalidIfcMetadataNotes.Count;
                if (status.InvalidIfcMetadataCount > 0)
                {
                    _diagnostics.AddWarning("IfcValidation", "Invalid IFC entity/predefined type combinations were found.", new
                    {
                        status.InvalidIfcMetadataCount,
                        Notes = status.InvalidIfcMetadataNotes.Take(40).ToList()
                    });
                }

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
                if (status.InvalidIfcMetadataCount > 0)
                {
                    errors.Add("Invalid IFC entity/predefined combinations: " + status.InvalidIfcMetadataCount);
                }

                status.ErrorDetails = string.Join(" | ", errors);
                var resourcesOk = status.SharedParameterFileFound &&
                                  status.EntityMappingLoaded &&
                                  status.ClassificationSlotsLoaded &&
                                  string.IsNullOrWhiteSpace(status.ErrorDetails);
                status.Message = resourcesOk ? "Resource diagnostics: OK" : "Resource diagnostics: Error";
                UpdateDiagnosticsSummaryFromSetup(doc, status);
            }
            catch (Exception ex)
            {
                status.Message = "Setup check failed.";
                status.ErrorDetails = ex.Message;
                _diagnostics.AddError("CheckSetup", "Setup diagnostics failed.", ex);
            }

            _diagnostics.AddInfo("CheckSetup", "Setup diagnostics completed.");
            return status;
        }

        public SetupStatus AssignParameters(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            _diagnostics.AddInfo("AssignParameters", "Assign parameters request started.");
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

            UpdateDiagnosticsSummaryFromSetup(doc, status);
            _diagnostics.AddInfo("AssignParameters", "Assign parameters request completed.", new
            {
                status.ParametersRequestedCount,
                status.InsertSucceededCount,
                status.ReInsertSucceededCount,
                status.VerifiedBoundCount
            });
            return status;
        }

        public DiagnosticsState GetDiagnosticsState()
        {
            return _diagnostics.Snapshot();
        }

        public void ClearDiagnostics()
        {
            _diagnostics.Clear();
        }

        public void RunFullDiagnostics(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            _diagnostics.Clear();
            _diagnostics.AddInfo("FullDiagnostics", "Started full diagnostics run.");
            LogEnvironmentAndSharedParameterPath(doc);
            RunSharedParameterFileInspection(doc);
            RunExpectedDefinitionDiagnostics(doc);
            RunCategoryBindingDiagnostics(doc, selectedCategoryIds);
            RunBindingAttemptDiagnostics(doc, selectedCategoryIds);
            RunSingleParameterBindDiagnostic(doc, selectedCategoryIds, "IFCName");
            var setup = CheckSetup(doc, selectedCategoryIds);
            UpdateDiagnosticsSummaryFromSetup(doc, setup);
            _diagnostics.AddInfo("FullDiagnostics", "Completed full diagnostics run.");
        }

        public void RunSharedParameterFileInspection(Document doc)
        {
            LogEnvironmentAndSharedParameterPath(doc);
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var inspect = _fileInspector.Inspect(sharedPath);
            _diagnostics.Summary.SharedParameterPath = sharedPath;
            _diagnostics.Summary.SharedParameterFileExists = inspect.FileExists;
            _diagnostics.AddInfo("SharedParameterFile", "Inspecting shared parameter file.", new
            {
                path = sharedPath,
                inspect.FileExists,
                inspect.IsReadable,
                inspect.FileLength,
                inspect.LastWriteTimeUtc
            });

            if (!inspect.FileExists)
            {
                _diagnostics.AddError("SharedParameterFile", "Shared parameter file does not exist.");
                return;
            }

            if (!inspect.IsReadable)
            {
                _diagnostics.AddError("SharedParameterFile", "Shared parameter file is not readable.", null, new { inspect.ReadError });
                return;
            }

            _diagnostics.AddDebug("SharedParameterFile", "First lines preview.", new { inspect.PreviewLines });
            _diagnostics.AddInfo("SharedParameterFile", "Manual parse complete.", new
            {
                GroupCount = inspect.Groups.Count,
                DefinitionCount = inspect.Parameters.Count,
                Groups = inspect.Groups.Select(g => g.Name).ToList()
            });

            var file = TryOpenSharedParameterFileWithDiagnostics(doc, "SharedParameterFile");
            if (file == null)
            {
                return;
            }

            var groups = file.Groups.Cast<DefinitionGroup>().ToList();
            _diagnostics.Summary.GroupCount = groups.Count;
            if (groups.Count == 0)
            {
                _diagnostics.AddError("SharedParameterFile", "OpenSharedParameterFile succeeded but no groups were found.");
            }

            var definitionCount = 0;
            foreach (var group in groups)
            {
                var definitions = group.Definitions.Cast<Definition>().ToList();
                definitionCount += definitions.Count;
                _diagnostics.AddInfo("SharedParameterFile", "Group discovered.", new
                {
                    Group = group.Name,
                    DefinitionCount = definitions.Count,
                    Definitions = definitions.Select(d => new
                    {
                        d.Name,
                        DataType = d.GetDataType().TypeId,
                        Guid = (d as ExternalDefinition)?.GUID.ToString()
                    }).ToList()
                });
            }

            _diagnostics.Summary.DefinitionCount = definitionCount;
            _diagnostics.AddInfo("SharedParameterFile", "Revit API parse complete.", new { GroupCount = groups.Count, DefinitionCount = definitionCount });
        }

        public void RunExpectedDefinitionDiagnostics(Document doc)
        {
            var file = TryOpenSharedParameterFileWithDiagnostics(doc, "ExpectedDefinitions");
            if (file == null)
            {
                return;
            }

            var groups = file.Groups.Cast<DefinitionGroup>().ToList();
            var lookup = BuildSharedParameterLookup(groups);
            _diagnostics.AddInfo("ExpectedDefinitions", "Shared parameter groups available for lookup.", new
            {
                GroupCount = groups.Count,
                Groups = groups.Select(g => new
                {
                    Group = g.Name,
                    DefinitionCount = g.Definitions.Size
                }).ToList()
            });
            _diagnostics.Summary.TotalExpectedParameters = ExpectedParameters.Length;

            foreach (var expected in ExpectedParameters)
            {
                var match = ResolveDefinitionAcrossGroups(lookup, expected.LookupNames, null);
                var inAnyGroup = match != null;
                var ciMatches = lookup.Values
                    .Select(v => v.Definition.Name)
                    .Where(n => expected.LookupNames.Any(l => string.Equals(l, n, StringComparison.OrdinalIgnoreCase)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var trimmedMatches = lookup.Values
                    .Select(v => v.Definition.Name)
                    .Where(n => expected.LookupNames.Any(l => string.Equals(l?.Trim(), n?.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (match != null)
                {
                    _diagnostics.Summary.TotalParametersFound++;
                    _diagnostics.Summary.LastSuccessfulParameterFound = expected.DisplayName;
                }

                var resolvedName = match?.Definition?.Name;
                var fallbackUsed = !string.IsNullOrWhiteSpace(resolvedName) &&
                                   !string.Equals(resolvedName, expected.DisplayName, StringComparison.Ordinal);
                if (fallbackUsed)
                {
                    _diagnostics.AddWarning("ExpectedDefinitions", "Legacy compatibility alias used.", new
                    {
                        Requested = expected.DisplayName,
                        Resolved = resolvedName
                    });
                }

                _diagnostics.AddInfo("ExpectedDefinitions", "Expected parameter lookup.", new
                {
                    Parameter = expected.DisplayName,
                    BindingScope = expected.ExpectedBindingType,
                    DefinitionExistsAnyGroup = inAnyGroup,
                    FoundGroup = match?.GroupName,
                    ResolvedName = resolvedName,
                    LegacyFallbackUsed = fallbackUsed,
                    NearMatchesCaseInsensitive = ciMatches,
                    NearMatchesTrimmed = trimmedMatches
                });
            }
        }

        public void RunCategoryBindingDiagnostics(Document doc, IList<ElementId> selectedCategoryIds = null)
        {
            var selectedIds = selectedCategoryIds == null || selectedCategoryIds.Count == 0
                ? null
                : new HashSet<long>(selectedCategoryIds.Select(x => x.Value));
            var included = new List<string>();
            var skipped = new List<object>();
            foreach (var category in doc.Settings.Categories.Cast<Category>().Where(c => c != null).OrderBy(c => c.Name))
            {
                if (selectedIds != null && !selectedIds.Contains(category.Id.Value))
                {
                    skipped.Add(new { category.Name, Reason = "Not selected in UI." });
                    continue;
                }

                if (!category.AllowsBoundParameters)
                {
                    skipped.Add(new { category.Name, Reason = "AllowsBoundParameters=false." });
                    continue;
                }

                if (category.IsTagCategory)
                {
                    skipped.Add(new { category.Name, Reason = "Tag category." });
                    continue;
                }

                if (category.CategoryType == CategoryType.Internal)
                {
                    skipped.Add(new { category.Name, Reason = "Internal category." });
                    continue;
                }

                included.Add(category.Name);
            }

            _diagnostics.AddInfo("CategoryResolution", "Category resolution complete.", new
            {
                RequestedCount = selectedIds?.Count ?? doc.Settings.Categories.Size,
                Included = included,
                Skipped = skipped,
                FinalCategorySetCount = included.Count
            });
        }

        public void RunSingleParameterBindDiagnostic(Document doc, IList<ElementId> selectedCategoryIds, string parameterName)
        {
            var spec = ExpectedParameters.FirstOrDefault(x => string.Equals(x.DisplayName, parameterName, StringComparison.OrdinalIgnoreCase))
                       ?? ExpectedParameters.First();
            _diagnostics.AddInfo("SingleParameterBind", "Testing single parameter binding.", new { RequestedParameter = parameterName, TargetParameter = spec.DisplayName });
            try
            {
                var file = TryOpenSharedParameterFileWithDiagnostics(doc, "SingleParameterBind");
                if (file == null)
                {
                    return;
                }

                var groups = file.Groups.Cast<DefinitionGroup>().ToList();
                var lookup = BuildSharedParameterLookup(groups);
                _diagnostics.AddInfo("SingleParameterBind", "Available groups and definition counts.", new
                {
                    Groups = groups.Select(g => new { Group = g.Name, DefinitionCount = g.Definitions.Size }).ToList()
                });
                var match = ResolveDefinitionAcrossGroups(lookup, spec.LookupNames, null);
                _diagnostics.AddInfo("SingleParameterBind", "Single parameter lookup result.", new
                {
                    Parameter = spec.DisplayName,
                    Candidates = spec.LookupNames,
                    MatchFound = match != null,
                    MatchedGroup = match?.GroupName,
                    MatchedDefinition = match?.Definition?.Name
                });
                if (match == null)
                {
                    _diagnostics.AddError("SingleParameterBind", "Definition could not be resolved for single parameter test.", null, new { spec.DisplayName, Candidates = spec.LookupNames });
                    return;
                }

                var categories = GetModelCategories(doc, selectedCategoryIds, out _).Take(5).ToList();
                var catSet = doc.Application.Create.NewCategorySet();
                foreach (var category in categories)
                {
                    catSet.Insert(category);
                }

                var existingBindingPresent = TryResolveBinding(GetBindingMap(doc), spec.LookupNames, out var existingDefinitionName, out var existingBinding);
                var existingBindingKind = existingBinding is TypeBinding ? "type" : existingBinding is InstanceBinding ? "instance" : "n/a";
                var existingBindingCategoryCount = existingBinding?.Categories?.Size ?? 0;
                var binding = spec.ExpectedBindingType == "type"
                    ? (Binding)doc.Application.Create.NewTypeBinding(catSet)
                    : doc.Application.Create.NewInstanceBinding(catSet);

                var insert = false;
                var reinsert = false;
                var transactionStarted = false;
                var transactionCommitted = false;
                var transactionRolledBack = false;
                ExecuteDiagnosticBindingTransaction(doc, "DfE IFC Namer - Single Parameter Diagnostic", () =>
                {
                    transactionStarted = true;
                    _diagnostics.AddInfo("SingleParameterBind", "Diagnostic transaction started.", new { Parameter = spec.DisplayName });
                    insert = doc.ParameterBindings.Insert(match.Definition, binding, GroupTypeId.Ifc);
                    _diagnostics.AddInfo("SingleParameterBind", "Insert attempted.", new { Parameter = spec.DisplayName, InsertResult = insert });
                    if (!insert)
                    {
                        reinsert = doc.ParameterBindings.ReInsert(match.Definition, binding, GroupTypeId.Ifc);
                        _diagnostics.AddInfo("SingleParameterBind", "ReInsert attempted.", new { Parameter = spec.DisplayName, ReInsertResult = reinsert });
                    }
                },
                committed: () =>
                {
                    transactionCommitted = true;
                    _diagnostics.AddInfo("SingleParameterBind", "Diagnostic transaction committed.");
                },
                rolledBack: () =>
                {
                    transactionRolledBack = true;
                    _diagnostics.AddInfo("SingleParameterBind", "Diagnostic transaction rolled back.");
                },
                rollbackAtEnd: true);

                var verified = TryResolveBinding(GetBindingMap(doc), spec.LookupNames, out _, out _);

                if (insert || reinsert)
                {
                    _diagnostics.Summary.LastSuccessfulBinding = spec.DisplayName;
                    _diagnostics.Summary.TotalInsertSuccesses += insert ? 1 : 0;
                    _diagnostics.Summary.TotalReInsertSuccesses += reinsert ? 1 : 0;
                }
                else
                {
                    _diagnostics.Summary.LastFailedBinding = spec.DisplayName;
                }

                if (verified)
                {
                    _diagnostics.Summary.TotalVerified += 1;
                }

                _diagnostics.AddInfo("SingleParameterBind", "Single parameter test complete.", new
                {
                    Parameter = spec.DisplayName,
                    BindingKind = spec.ExpectedBindingType,
                    Categories = categories.Select(c => c.Name).ToList(),
                    CategoryCount = categories.Count,
                    RequestedName = spec.DisplayName,
                    ResolvedDefinitionName = match.Definition.Name,
                    ResolvedGroup = match.GroupName,
                    ExistingBindingPresent = existingBindingPresent,
                    ExistingBindingKind = existingBindingKind,
                    ExistingBindingDefinition = existingDefinitionName,
                    ExistingBindingCategoryCount = existingBindingCategoryCount,
                    TransactionStarted = transactionStarted,
                    TransactionCommitted = transactionCommitted,
                    TransactionRolledBack = transactionRolledBack,
                    Insert = insert,
                    ReInsert = reinsert,
                    Verified = verified
                });
            }
            catch (Exception ex)
            {
                _diagnostics.Summary.LastFailedBinding = spec.DisplayName;
                _diagnostics.AddError("SingleParameterBind", "Single parameter bind test failed.", ex);
            }
        }

        private static Dictionary<string, SharedDefinitionMatch> BuildSharedParameterLookup(IEnumerable<DefinitionGroup> groups)
        {
            var lookup = new Dictionary<string, SharedDefinitionMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                foreach (var definition in group.Definitions.Cast<Definition>().OfType<ExternalDefinition>())
                {
                    if (!lookup.ContainsKey(definition.Name))
                    {
                        lookup[definition.Name] = new SharedDefinitionMatch(definition, group.Name);
                    }

                    var guidKey = definition.GUID.ToString("D");
                    if (!lookup.ContainsKey(guidKey))
                    {
                        lookup[guidKey] = new SharedDefinitionMatch(definition, group.Name);
                    }
                }
            }

            return lookup;
        }

        private static SharedDefinitionMatch ResolveDefinitionAcrossGroups(
            IReadOnlyDictionary<string, SharedDefinitionMatch> lookup,
            IEnumerable<string> candidateNames,
            string preferredGuid)
        {
            if (!string.IsNullOrWhiteSpace(preferredGuid) &&
                lookup.TryGetValue(preferredGuid, out var guidMatch))
            {
                return guidMatch;
            }

            foreach (var candidate in candidateNames ?? Enumerable.Empty<string>())
            {
                if (lookup.TryGetValue(candidate, out var nameMatch))
                {
                    return nameMatch;
                }
            }

            return null;
        }

        private static void ExecuteDiagnosticBindingTransaction(
            Document doc,
            string transactionName,
            Action operation,
            Action committed,
            Action rolledBack,
            bool rollbackAtEnd)
        {
            if (doc.IsModifiable)
            {
                using (var subTransaction = new SubTransaction(doc))
                {
                    subTransaction.Start();
                    try
                    {
                        operation();
                        if (rollbackAtEnd)
                        {
                            subTransaction.RollBack();
                            rolledBack?.Invoke();
                        }
                        else
                        {
                            subTransaction.Commit();
                            committed?.Invoke();
                        }
                    }
                    catch
                    {
                        if (subTransaction.GetStatus() == TransactionStatus.Started)
                        {
                            subTransaction.RollBack();
                            rolledBack?.Invoke();
                        }

                        throw;
                    }
                }

                return;
            }

            using (var transaction = new Transaction(doc, transactionName))
            {
                transaction.Start();
                try
                {
                    operation();
                    if (rollbackAtEnd)
                    {
                        transaction.RollBack();
                        rolledBack?.Invoke();
                    }
                    else
                    {
                        transaction.Commit();
                        committed?.Invoke();
                    }
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                        rolledBack?.Invoke();
                    }

                    throw;
                }
            }
        }

        private DefinitionFile TryOpenSharedParameterFileWithDiagnostics(Document doc, string stage)
        {
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var inspection = _fileInspector.Inspect(sharedPath, 10);
            _diagnostics.AddInfo(stage, "Shared parameter preflight before OpenSharedParameterFile.", new
            {
                Path = sharedPath,
                inspection.FileExists,
                inspection.IsReadable,
                inspection.FileLength,
                inspection.LastWriteTimeUtc,
                PreviewLines = inspection.PreviewLines
            });

            _diagnostics.Summary.SharedParameterPath = sharedPath;
            _diagnostics.Summary.SharedParameterFileExists = inspection.FileExists;

            if (!inspection.FileExists || !inspection.IsReadable)
            {
                _diagnostics.Summary.OpenSharedParameterFileSucceeded = false;
                _diagnostics.AddError(stage, "Shared parameter file preflight failed.", null, new { inspection.ReadError });
                return null;
            }

            var before = doc.Application.SharedParametersFilename;
            doc.Application.SharedParametersFilename = sharedPath;
            var after = doc.Application.SharedParametersFilename;
            _diagnostics.AddInfo(stage, "Set Application.SharedParametersFilename.", new { Before = before, After = after });
            try
            {
                var file = doc.Application.OpenSharedParameterFile();
                var hasGroups = file != null && file.Groups != null && file.Groups.Size > 0;
                _diagnostics.Summary.OpenSharedParameterFileSucceeded = hasGroups;
                if (file == null)
                {
                    _diagnostics.AddError(stage, "OpenSharedParameterFile returned null.");
                }
                else if (!hasGroups)
                {
                    _diagnostics.AddError(stage, "OpenSharedParameterFile succeeded but no groups were found.");
                }

                return file;
            }
            catch (Exception ex)
            {
                _diagnostics.Summary.OpenSharedParameterFileSucceeded = false;
                _diagnostics.AddError(stage, "OpenSharedParameterFile threw an exception.", ex);
                return null;
            }
        }

        private sealed class SharedDefinitionMatch
        {
            public SharedDefinitionMatch(ExternalDefinition definition, string groupName)
            {
                Definition = definition;
                GroupName = groupName;
            }

            public ExternalDefinition Definition { get; }
            public string GroupName { get; }
        }

        private void RunBindingAttemptDiagnostics(Document doc, IList<ElementId> selectedCategoryIds)
        {
            var selectedCategories = selectedCategoryIds?.Select(id => Category.GetCategory(doc, id)).Where(c => c != null).ToList();
            try
            {
                _diagnostics.AddInfo("BindingAttempt", "Starting diagnostic binding transaction.");
                ExecuteDiagnosticBindingTransaction(doc, "DfE IFC Namer - Diagnostics Binding Attempt", () =>
                {
                    var bindingSummary = _parameterService.EnsureIfcNameParameters(doc, selectedCategories);
                    foreach (var result in bindingSummary.ParameterResults)
                    {
                        _diagnostics.AddInfo("BindingAttempt", "Binding attempt result.", new
                        {
                            RequestedName = result.Name,
                            Resolved = result.FoundInSharedParameterFile,
                            BindingType = result.ExpectedBindingType,
                            CategoryCount = bindingSummary.IncludedCategoriesCount,
                            ParameterGroup = result.ExpectedBindingType == "project info" ? GroupTypeId.Data.TypeId : GroupTypeId.Ifc.TypeId,
                            InsertAttempted = true,
                            result.InsertSucceeded,
                            result.ReInsertSucceeded,
                            ExistingBindingWasPresent = !result.InsertSucceeded,
                            result.FinalBoundState,
                            result.Notes
                        });
                    }
                },
                committed: () => _diagnostics.AddDebug("BindingAttempt", "Diagnostic binding transaction committed."),
                rolledBack: () => _diagnostics.AddDebug("BindingAttempt", "Diagnostic binding transaction rolled back."),
                rollbackAtEnd: true);
            }
            catch (Exception ex)
            {
                _diagnostics.AddError("BindingAttempt", "Binding attempt diagnostics failed.", ex);
            }
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
            _diagnostics.AddInfo("Sync", "COBie sync started.", new
            {
                settings.Scope,
                settings.OverwriteMode,
                settings.InstanceSource,
                settings.InstanceTarget,
                settings.TypeSource,
                settings.TypeTarget
            });
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

            _diagnostics.AddInfo("Sync", "COBie sync completed.", new
            {
                result.InstancesUpdated,
                result.InstancesSkipped,
                result.InstancesFailed,
                result.TypesUpdated,
                result.TypesSkipped,
                result.TypesFailed
            });
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

        private void LogEnvironmentAndSharedParameterPath(Document doc)
        {
            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var addinFolder = _parameterService.ResolveAddinFolder();
                var sharedPath = _parameterService.ResolveSharedParameterFilePath();
                var resourcesFolder = Path.Combine(addinFolder, "Resources");
                var exists = File.Exists(sharedPath);
                var fileInfo = exists ? new FileInfo(sharedPath) : null;
                _diagnostics.AddInfo("Environment", "Resolved runtime paths.", new
                {
                    AssemblyLocation = assemblyPath,
                    AddinFolder = addinFolder,
                    ResourcesFolder = resourcesFolder,
                    SharedParameterFilePath = sharedPath,
                    FileExists = exists,
                    FileLengthBytes = fileInfo?.Length ?? 0L,
                    LastModifiedUtc = fileInfo?.LastWriteTimeUtc
                });

                _diagnostics.Summary.DocumentTitle = doc.Title;
                _diagnostics.Summary.ActiveProjectName = doc.ProjectInformation?.Name;
                _diagnostics.Summary.RevitVersion = doc.Application.VersionNumber;
                _diagnostics.Summary.SharedParameterPath = sharedPath;
            }
            catch (Exception ex)
            {
                _diagnostics.AddError("Environment", "Failed while collecting environment diagnostics.", ex);
            }
        }

        private void UpdateDiagnosticsSummaryFromSetup(Document doc, SetupStatus status)
        {
            _diagnostics.Summary.DocumentTitle = doc.Title;
            _diagnostics.Summary.ActiveProjectName = doc.ProjectInformation?.Name;
            _diagnostics.Summary.RevitVersion = doc.Application.VersionNumber;
            _diagnostics.Summary.SharedParameterPath = status?.SharedParameterFilePath;
            _diagnostics.Summary.SharedParameterFileExists = status?.SharedParameterFileFound ?? false;
            _diagnostics.Summary.LastRunTimeUtc = DateTime.UtcNow;
            _diagnostics.Summary.TotalExpectedParameters = status?.ParametersRequestedCount ?? 0;
            _diagnostics.Summary.TotalParametersFound = status?.ParametersFoundInSharedFileCount ?? 0;
            _diagnostics.Summary.TotalInsertSuccesses = status?.InsertSucceededCount ?? 0;
            _diagnostics.Summary.TotalReInsertSuccesses = status?.ReInsertSucceededCount ?? 0;
            _diagnostics.Summary.TotalVerified = status?.VerifiedBoundCount ?? 0;
            _diagnostics.Summary.IfcClassesLoaded = status?.IfcClassesLoadedCount ?? 0;
            _diagnostics.Summary.IfcPredefinedTypesLoaded = status?.IfcPredefinedTypesLoadedCount ?? 0;
            _diagnostics.Summary.InvalidIfcMetadataCount = status?.InvalidIfcMetadataCount ?? 0;
            _diagnostics.Summary.LastSuccessfulBinding = status?.ParameterResults?.FirstOrDefault(r => r.InsertSucceeded || r.ReInsertSucceeded)?.Name;
            _diagnostics.Summary.LastFailedBinding = status?.ParameterResults?.FirstOrDefault(r => !r.FinalBoundState)?.Name;
            _diagnostics.Summary.LastSuccessfulParameterFound = status?.ParameterResults?.FirstOrDefault(r => r.FoundInSharedParameterFile)?.Name;
            if (!string.IsNullOrWhiteSpace(status?.ErrorDetails))
            {
                _diagnostics.Summary.LastErrorSummary = status.ErrorDetails;
            }

            _diagnostics.AddInfo("SetupSummary", "Setup/assign summary captured.", new
            {
                status?.SharedParameterFileFound,
                status?.IncludedCategoriesCount,
                status?.SkippedUnsupportedCategoriesCount,
                status?.ParametersRequestedCount,
                status?.ParametersFoundInSharedFileCount,
                status?.InsertSucceededCount,
                status?.ReInsertSucceededCount,
                status?.VerifiedBoundCount,
                status?.VerificationFailedCount,
                status?.ErrorDetails
            });
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

        private static string AppendNote(string existing, string note)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return note;
            }

            return existing + " " + note;
        }

        private IList<IfcEntityDefinition> SafeLoadEntityLibrary(out string error)
        {
            try
            {
                var loaded = _resourceJsonService.LoadEntityLibrary() ?? new List<IfcEntityDefinition>();
                error = null;
                _diagnostics.AddInfo("IfcMatrix", "Loaded IFC entity mapping JSON.", new
                {
                    ClassCount = loaded.Count,
                    PredefinedTypeCount = loaded.Sum(x => x.PredefinedTypes?.Count ?? 0),
                    Path = _resourceJsonService.ResolveEntityMappingPath(),
                    Matrix = loaded.Select(x => new
                    {
                        x.DisplayName,
                        x.IFCClassToken,
                        x.ExportAs,
                        x.ExportType,
                        x.NameFormat,
                        PredefinedTypes = x.PredefinedTypes ?? new List<string>()
                    }).ToList()
                });
                return loaded;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _diagnostics.AddError("IfcMatrix", "Failed to load IFC entity mapping JSON.", ex, new
                {
                    Path = _resourceJsonService.ResolveEntityMappingPath()
                });
                return new List<IfcEntityDefinition>();
            }
        }

        private static IList<string> ValidateIfcMetadataAgainstLibrary(Document doc, IList<IfcEntityDefinition> library)
        {
            var notes = new List<string>();
            if (library == null || library.Count == 0)
            {
                notes.Add("IFC entity mapping JSON is empty; cannot validate IFC metadata values.");
                return notes;
            }

            var entityRules = library
                .Where(x => !string.IsNullOrWhiteSpace(x.IFCClassToken))
                .ToDictionary(
                    x => x.IFCClassToken,
                    x => new HashSet<string>((x.PredefinedTypes ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Take(1000)
                .ToList();

            foreach (var element in elements)
            {
                var entity = LookupFirst(element, "DfE_IFCEntity")?.AsString();
                var predefined = LookupFirst(element, "DfE_IFCPredefinedType")?.AsString();
                var userDefined = LookupFirst(element, "DfE_UserDefinedPredefinedTypeValue")?.AsString();

                if (string.IsNullOrWhiteSpace(entity) && string.IsNullOrWhiteSpace(predefined) && string.IsNullOrWhiteSpace(userDefined))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entity))
                {
                    notes.Add($"{element.Id}: DfE_IFCEntity is empty while IFC metadata exists.");
                    continue;
                }

                if (!entityRules.TryGetValue(entity.Trim(), out var allowed))
                {
                    notes.Add($"{element.Id}: DfE_IFCEntity '{entity}' is not in JSON-supported IFC classes.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(predefined))
                {
                    notes.Add($"{element.Id}: DfE_IFCPredefinedType is empty for entity '{entity}'.");
                    continue;
                }

                if (!allowed.Contains(predefined.Trim()))
                {
                    notes.Add($"{element.Id}: DfE_IFCPredefinedType '{predefined}' is invalid for entity '{entity}'.");
                    continue;
                }

                if (string.Equals(predefined.Trim(), "USERDEFINED", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(userDefined))
                    {
                        notes.Add($"{element.Id}: USERDEFINED selected but DfE_UserDefinedPredefinedTypeValue is blank.");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(userDefined))
                {
                    notes.Add($"{element.Id}: DfE_UserDefinedPredefinedTypeValue should be blank when predefined type is '{predefined}'.");
                }
            }

            return notes;
        }

        private static Parameter LookupFirst(Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var parameter = element.LookupParameter(name);
                if (parameter != null)
                {
                    return parameter;
                }
            }

            return null;
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

        private Dictionary<string, SharedDefinitionMatch> ResolveExpectedDefinitionsInSharedFile(Document doc)
        {
            var resolved = new Dictionary<string, SharedDefinitionMatch>(StringComparer.OrdinalIgnoreCase);
            var file = TryOpenSharedParameterFileWithDiagnostics(doc, "SetupSharedDefinitionLookup");
            if (file == null)
            {
                return resolved;
            }

            var groups = file.Groups.Cast<DefinitionGroup>().ToList();
            var lookup = BuildSharedParameterLookup(groups);
            _diagnostics.AddInfo("SetupSharedDefinitionLookup", "Setup lookup groups.", new
            {
                GroupCount = groups.Count,
                Groups = groups.Select(g => new { Group = g.Name, DefinitionCount = g.Definitions.Size }).ToList()
            });

            foreach (var expected in ExpectedParameters)
            {
                var match = ResolveDefinitionAcrossGroups(lookup, expected.LookupNames, null);
                if (match != null)
                {
                    resolved[expected.DisplayName] = match;
                }

                _diagnostics.AddInfo("SetupSharedDefinitionLookup", "Setup shared definition lookup result.", new
                {
                    Parameter = expected.DisplayName,
                    Candidates = expected.LookupNames,
                    MatchFound = match != null,
                    ResolvedName = match?.Definition?.Name,
                    MatchedGroup = match?.GroupName
                });
            }

            return resolved;
        }

        private static IList<ParameterBindingResult> BuildVerificationResults(
            Document doc,
            IList<Category> modelCategories,
            IReadOnlyDictionary<string, SharedDefinitionMatch> sharedDefinitionMatches)
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
                    FoundInSharedParameterFile = sharedDefinitionMatches.ContainsKey(expected.DisplayName),
                    BindingAction = "Verify"
                };

                if (sharedDefinitionMatches.TryGetValue(expected.DisplayName, out var resolved))
                {
                    result.Notes = $"ResolvedName='{resolved.Definition.Name}', ResolvedGroup='{resolved.GroupName}'.";
                }
                else
                {
                    result.Notes = "Parameter not found in any shared parameter group.";
                }

                if (!TryResolveBinding(bindingMap, expected.LookupNames, out var definitionName, out var binding))
                {
                    result.FinalBoundState = false;
                    result.Notes = AppendNote(result.Notes, "Definition not bound in document.");
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
                    result.Notes = AppendNote(result.Notes, $"Binding kind mismatch for definition '{definitionName}'.");
                }
                else if (!categoriesOk)
                {
                    result.Notes = AppendNote(result.Notes, "Binding categories do not match expected scope.");
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
