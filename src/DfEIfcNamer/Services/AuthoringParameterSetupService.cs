using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class AuthoringParameterSetupService
    {
        private readonly ParameterService _parameterService;

        public AuthoringParameterSetupService(ParameterService parameterService)
        {
            _parameterService = parameterService;
        }

        public SetupCheckResult Check(Document doc, IList<ElementId> categoryIds)
        {
            var manifestEntries = ParameterBindingManifest.All();
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var statusRows = BuildStatusRows(doc, categoryIds, false, new Dictionary<string, ParameterScopeKind>(StringComparer.OrdinalIgnoreCase));
            return new SetupCheckResult
            {
                Status = statusRows.Any(x => !x.Exists || x.Scope != x.ActualScope) ? "Warning" : "Ready",
                Notes = $"Verified {statusRows.Count} shared parameters across all groups.",
                Parameters = statusRows,
                ManifestLoaded = manifestEntries.Any(),
                ManifestEntriesCount = manifestEntries.Count,
                SharedParameterFileLoaded = doc.Application.OpenSharedParameterFile() != null,
                SharedParameterSource = sharedPath,
                SharedParameterDefinitionsCount = statusRows.Count(x => x.ParameterName != "<row-error>" && x.ParameterName != "<exception>"),
                MatchedSharedParameterDefinitionsCount = statusRows.Count(x => x.FoundInSharedParameterFile),
                ProjectedRowsCount = statusRows.Count,
                RowLevelErrors = statusRows.Where(x => x.ParameterName == "<row-error>").Select(x => x.Notes).ToList(),
                Exceptions = statusRows.Where(x => x.ParameterName == "<exception>").Select(x => x.Notes).ToList(),
                ManifestSource = "embedded"
            };
        }

        public SetupCheckResult CreateMissing(Document doc, IList<ElementId> categoryIds)
        {
            var manifestEntries = ParameterBindingManifest.All();
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var overrides = new Dictionary<string, ParameterScopeKind>(StringComparer.OrdinalIgnoreCase);
            var rows = BuildStatusRows(doc, categoryIds, true, overrides);
            return new SetupCheckResult
            {
                Status = rows.Any(x => !x.Exists) ? "Warning" : "Ready",
                Notes = $"Create/bind attempted for {rows.Count} parameters resolved from shared file + manifest.",
                Parameters = rows,
                ManifestLoaded = manifestEntries.Any(),
                ManifestEntriesCount = manifestEntries.Count,
                SharedParameterFileLoaded = doc.Application.OpenSharedParameterFile() != null,
                SharedParameterSource = sharedPath,
                SharedParameterDefinitionsCount = rows.Count(x => x.ParameterName != "<row-error>" && x.ParameterName != "<exception>"),
                MatchedSharedParameterDefinitionsCount = rows.Count(x => x.FoundInSharedParameterFile),
                ProjectedRowsCount = rows.Count,
                RowLevelErrors = rows.Where(x => x.ParameterName == "<row-error>").Select(x => x.Notes).ToList(),
                Exceptions = rows.Where(x => x.ParameterName == "<exception>").Select(x => x.Notes).ToList(),
                ManifestSource = "embedded"
            };
        }

        private IList<RequiredParameterStatus> BuildStatusRows(Document doc, IList<ElementId> categoryIds, bool createMissing, IReadOnlyDictionary<string, ParameterScopeKind> scopeOverrides)
        {
            var manifest = ParameterBindingManifest.All();
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            var rowErrors = new List<string>();
            if (!ParameterService.EnsureSharedParameterFileConfigured(doc.Application, sharedPath, out var configureError))
            {
                var failedRows = manifest.Select(m => new RequiredParameterStatus
                {
                    ParameterName = m.Name,
                    Scope = m.Scope.ToString(),
                    Exists = false,
                    Writable = false,
                    FoundInSharedParameterFile = false,
                    Result = "Failed",
                    Action = "skip",
                    Notes = configureError
                }).ToList();
                StampDiagnostics(failedRows, manifest, 0, 0, sharedPath, false, rowErrors, new[] { configureError });
                return failedRows;
            }

            var sharedFile = doc.Application.OpenSharedParameterFile();
            var grouped = new Dictionary<string, (Definition Definition, string Group)>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in sharedFile?.Groups?.Cast<DefinitionGroup>() ?? Enumerable.Empty<DefinitionGroup>())
            {
                foreach (var definition in group.Definitions.Cast<Definition>())
                {
                    try
                    {
                        if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                        {
                            rowErrors.Add($"Shared parameter row skipped due to missing definition metadata in group '{group?.Name ?? "<null>"}'.");
                            continue;
                        }

                        if (!grouped.ContainsKey(definition.Name)) grouped[definition.Name] = (definition, group?.Name ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        rowErrors.Add($"Shared parameter parse failure in group '{group?.Name ?? "<null>"}': {ex.Message}");
                    }
                }
            }

            var categories = ResolveCategories(doc, categoryIds);
            var modelCategorySet = doc.Application.Create.NewCategorySet();
            foreach (var c in categories) modelCategorySet.Insert(c);
            var projectCategorySet = doc.Application.Create.NewCategorySet();
            var projectInfoCat = Category.GetCategory(doc, BuiltInCategory.OST_ProjectInformation);
            if (projectInfoCat != null) projectCategorySet.Insert(projectInfoCat);

            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
            while (iterator.MoveNext())
            {
                var definition = iterator.Key as Definition;
                var binding = iterator.Current as Binding;
                if (definition != null && binding != null && !bindings.ContainsKey(definition.Name)) bindings.Add(definition.Name, binding);
            }

            var sharedNames = grouped.Keys.ToList();
            var rows = new List<RequiredParameterStatus>();
            using (var tx = createMissing ? new Transaction(doc, "DfE Create Authoring Parameters") : null)
            {
                tx?.Start();
                foreach (var sharedName in sharedNames)
                {
                    var entry = ParameterBindingManifest.FindByName(sharedName);
                    var aliases = new[] { sharedName }.Concat((entry?.Aliases ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    grouped.TryGetValue(sharedName, out var sharedMatch);
                    var expectedScope = scopeOverrides != null && scopeOverrides.TryGetValue(sharedName, out var overrideScope)
                        ? overrideScope
                        : entry?.Scope ?? ParameterScopeKind.Instance;

                    var existingParam = ResolveParameter(doc, aliases, expectedScope);
                    var actualScope = ResolveActualScope(bindings, aliases, existingParam, expectedScope);
                    var exists = existingParam != null;
                    var mismatch = exists && actualScope != expectedScope.ToString() && actualScope != "Unknown";
                    var action = !exists ? "create" : mismatch ? "replace" : "skip";
                    var result = exists ? (mismatch ? "ScopeMismatch" : "Verified") : "Missing";
                    var notes = exists ? "Bound." : "Not bound.";

                    if (createMissing && (!exists || mismatch) && sharedMatch.Definition != null)
                    {
                        try
                        {
                            Binding binding = expectedScope == ParameterScopeKind.Type
                                ? (Binding)doc.Application.Create.NewTypeBinding(modelCategorySet)
                                : doc.Application.Create.NewInstanceBinding(expectedScope == ParameterScopeKind.Project ? projectCategorySet : modelCategorySet);
                            var groupType = expectedScope == ParameterScopeKind.Project ? GroupTypeId.Data : GroupTypeId.Ifc;
                            var inserted = doc.ParameterBindings.Insert(sharedMatch.Definition, binding, groupType);
                            if (!inserted) inserted = doc.ParameterBindings.ReInsert(sharedMatch.Definition, binding, groupType);
                            existingParam = ResolveParameter(doc, aliases, expectedScope);
                            exists = existingParam != null;
                            result = inserted && exists ? (mismatch ? "Replaced" : "Created") : "Failed";
                            notes = inserted ? "Insert/ReInsert executed." : "Revit binding API returned false.";
                        }
                        catch (Exception ex)
                        {
                            result = "Failed";
                            notes = ex.Message;
                            rowErrors.Add($"Binding failure for '{sharedName}': {ex.Message}");
                        }
                    }

                    rows.Add(new RequiredParameterStatus
                    {
                        ParameterName = sharedName,
                        Scope = expectedScope.ToString(),
                        Exists = exists,
                        Writable = exists && !existingParam.IsReadOnly,
                        FoundInSharedParameterFile = true,
                        SharedParameterGroup = sharedMatch.Group,
                        ActualScope = actualScope,
                        Action = action,
                        Usage = entry?.Usage ?? "Unmapped",
                        ExpectedCategories = entry?.Categories == null ? "*" : string.Join(",", entry.Categories),
                        Result = result,
                        Notes = notes
                    });
                }
                tx?.Commit();
            }

            var ordered = rows.OrderBy(r => r.ParameterName).ToList();
            StampDiagnostics(ordered, manifest, grouped.Count, ordered.Count(x => x.FoundInSharedParameterFile), sharedPath, sharedFile != null, rowErrors, Array.Empty<string>());
            return ordered;
        }

        private static void StampDiagnostics(
            IList<RequiredParameterStatus> rows,
            IList<ParameterBindingManifestEntry> manifest,
            int sharedDefinitionCount,
            int matchedCount,
            string sharedPath,
            bool sharedLoaded,
            IList<string> rowErrors,
            IEnumerable<string> exceptions)
        {
            var header = $"ManifestEntries={manifest?.Count ?? 0}; SharedDefs={sharedDefinitionCount}; Matched={matchedCount}; ProjectedRows={rows?.Count ?? 0}; SharedFileLoaded={sharedLoaded}; SharedPath={sharedPath}";
            if (rows != null && rows.Count > 0)
            {
                rows[0].Notes = string.IsNullOrWhiteSpace(rows[0].Notes) ? header : rows[0].Notes + " | " + header;
            }

            foreach (var e in rowErrors ?? Enumerable.Empty<string>())
            {
                rows?.Add(new RequiredParameterStatus
                {
                    ParameterName = "<row-error>",
                    Scope = "n/a",
                    Result = "Failed",
                    Notes = e,
                    Action = "skip"
                });
            }

            foreach (var ex in exceptions ?? Enumerable.Empty<string>())
            {
                rows?.Add(new RequiredParameterStatus
                {
                    ParameterName = "<exception>",
                    Scope = "n/a",
                    Result = "Failed",
                    Notes = ex,
                    Action = "skip"
                });
            }
        }

        private static IList<Category> ResolveCategories(Document doc, IList<ElementId> categoryIds)
        {
            var selectedIds = categoryIds?.Select(x => x.Value).ToHashSet();
            return doc.Settings.Categories.Cast<Category>()
                .Where(c => c != null && c.AllowsBoundParameters && !c.IsTagCategory && c.CategoryType != CategoryType.Internal)
                .Where(c => selectedIds == null || selectedIds.Count == 0 || selectedIds.Contains(c.Id.Value))
                .ToList();
        }

        private static Parameter ResolveParameter(Document doc, IEnumerable<string> names, ParameterScopeKind scope)
        {
            if (scope == ParameterScopeKind.Project)
            {
                foreach (var name in names)
                {
                    var p = doc.ProjectInformation.LookupParameter(name);
                    if (p != null) return p;
                }
                return null;
            }

            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var element = collector.FirstOrDefault();
            if (scope == ParameterScopeKind.Type)
            {
                var typed = collector.FirstOrDefault(e => e.GetTypeId() != ElementId.InvalidElementId);
                var type = typed == null ? null : doc.GetElement(typed.GetTypeId());
                foreach (var name in names)
                {
                    var p = type?.LookupParameter(name);
                    if (p != null) return p;
                }
                return null;
            }

            foreach (var name in names)
            {
                var p = element?.LookupParameter(name);
                if (p != null) return p;
            }

            return null;
        }

        private static string ResolveActualScope(IReadOnlyDictionary<string, Binding> bindings, IEnumerable<string> aliases, Parameter existingParam, ParameterScopeKind expected)
        {
            if (expected == ParameterScopeKind.Project && existingParam != null) return ParameterScopeKind.Project.ToString();
            foreach (var alias in aliases)
            {
                if (bindings.TryGetValue(alias, out var binding)) return binding is TypeBinding ? ParameterScopeKind.Type.ToString() : ParameterScopeKind.Instance.ToString();
            }

            return existingParam == null ? "Missing" : "Unknown";
        }
    }
}
