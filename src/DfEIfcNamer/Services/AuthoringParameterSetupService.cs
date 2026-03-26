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
            var statusRows = BuildStatusRows(doc, categoryIds, createMissing: false);
            var result = new SetupCheckResult
            {
                Status = statusRows.Any(x => !x.Exists || x.Scope != x.ActualScope) ? "Warning" : "Ready",
                Notes = $"Verified {statusRows.Count} parameters using manifest + shared parameter file.",
                Parameters = statusRows
            };
            return result;
        }

        public SetupCheckResult CreateMissing(Document doc, IList<ElementId> categoryIds)
        {
            var rows = BuildStatusRows(doc, categoryIds, createMissing: true);
            return new SetupCheckResult
            {
                Status = rows.Any(x => !x.Exists) ? "Warning" : "Ready",
                Notes = $"Create/bind attempted for {rows.Count} manifest parameters.",
                Parameters = rows
            };
        }

        private IList<RequiredParameterStatus> BuildStatusRows(Document doc, IList<ElementId> categoryIds, bool createMissing)
        {
            var sharedPath = _parameterService.ResolveSharedParameterFilePath();
            if (!_parameterService.EnsureSharedParameterFileConfigured(doc.Application, sharedPath, out var configureError))
            {
                return ParameterBindingManifest.All().Select(m => new RequiredParameterStatus
                {
                    ParameterName = m.Name,
                    Scope = m.Scope.ToString(),
                    Exists = false,
                    Writable = false,
                    FoundInSharedParameterFile = false,
                    Result = "Failed",
                    Notes = configureError
                }).ToList();
            }

            var sharedFile = doc.Application.OpenSharedParameterFile();
            var grouped = new Dictionary<string, (Definition Definition, string Group)>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in sharedFile?.Groups?.Cast<DefinitionGroup>() ?? Enumerable.Empty<DefinitionGroup>())
            {
                foreach (var definition in group.Definitions.Cast<Definition>())
                {
                    if (!grouped.ContainsKey(definition.Name))
                    {
                        grouped[definition.Name] = (definition, group.Name);
                    }
                }
            }

            var categories = ResolveCategories(doc, categoryIds);
            var modelCategorySet = doc.Application.Create.NewCategorySet();
            foreach (var c in categories) modelCategorySet.Insert(c);
            var projectCategorySet = doc.Application.Create.NewCategorySet();
            var projectInfoCat = Category.GetCategory(doc, BuiltInCategory.OST_ProjectInformation);
            if (projectInfoCat != null) projectCategorySet.Insert(projectInfoCat);

            var rows = new List<RequiredParameterStatus>();
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
            while (iterator.MoveNext())
            {
                var definition = iterator.Key as Definition;
                var binding = iterator.Current as Binding;
                if (definition != null && binding != null && !bindings.ContainsKey(definition.Name)) bindings.Add(definition.Name, binding);
            }

            using (var tx = createMissing ? new Transaction(doc, "DfE Create Authoring Parameters") : null)
            {
                if (tx != null) tx.Start();
                foreach (var manifest in ParameterBindingManifest.All())
                {
                    var aliases = new[] { manifest.Name }.Concat(manifest.Aliases ?? Array.Empty<string>()).ToList();
                    var match = aliases.FirstOrDefault(grouped.ContainsKey);
                    grouped.TryGetValue(match ?? string.Empty, out var sharedMatch);
                    var expected = manifest.Scope.ToString();
                    var existingParam = ResolveParameter(doc, aliases, manifest.Scope);
                    var actualScope = ResolveActualScope(bindings, aliases, existingParam, manifest.Scope);
                    var exists = existingParam != null;
                    var writable = existingParam != null && !existingParam.IsReadOnly;
                    var result = exists ? "Verified" : "Missing";
                    var notes = exists ? "Bound." : "Not bound.";

                    if (createMissing && !exists && sharedMatch.Definition != null)
                    {
                        try
                        {
                            Binding binding = manifest.Scope == ParameterScopeKind.Type
                                ? (Binding)doc.Application.Create.NewTypeBinding(modelCategorySet)
                                : doc.Application.Create.NewInstanceBinding(manifest.Scope == ParameterScopeKind.Project ? projectCategorySet : modelCategorySet);
                            var groupType = manifest.Scope == ParameterScopeKind.Project ? GroupTypeId.Data : GroupTypeId.Ifc;
                            var inserted = doc.ParameterBindings.Insert(sharedMatch.Definition, binding, groupType);
                            if (!inserted) inserted = doc.ParameterBindings.ReInsert(sharedMatch.Definition, binding, groupType);
                            existingParam = ResolveParameter(doc, aliases, manifest.Scope);
                            exists = existingParam != null;
                            writable = exists && !existingParam.IsReadOnly;
                            result = inserted && exists ? "Created" : "Failed";
                            notes = inserted ? "Insert/ReInsert executed." : "Revit binding API returned false.";
                        }
                        catch (Exception ex)
                        {
                            result = "Failed";
                            notes = ex.Message;
                        }
                    }

                    rows.Add(new RequiredParameterStatus
                    {
                        ParameterName = manifest.Name,
                        Scope = expected,
                        Exists = exists,
                        Writable = writable,
                        FoundInSharedParameterFile = sharedMatch.Definition != null,
                        SharedParameterGroup = sharedMatch.Group,
                        ActualScope = actualScope,
                        Result = result,
                        Notes = notes
                    });
                }
                if (tx != null) tx.Commit();
            }

            return rows;
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
                if (bindings.TryGetValue(alias, out var binding))
                {
                    return binding is TypeBinding ? ParameterScopeKind.Type.ToString() : ParameterScopeKind.Instance.ToString();
                }
            }

            return existingParam == null ? "Missing" : "Unknown";
        }
    }
}
