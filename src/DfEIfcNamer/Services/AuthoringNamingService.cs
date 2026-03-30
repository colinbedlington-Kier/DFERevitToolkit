using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class AuthoringNamingService
    {
        private static readonly Regex InvalidIfcNameCharacters = new Regex(@"[^A-Za-z0-9_-]", RegexOptions.Compiled);
        private static readonly Regex InvalidIfcNameCharactersAllowDots = new Regex(@"[^A-Za-z0-9_.-]", RegexOptions.Compiled);
        private static readonly Regex UserDefinedSystemNameRegex = new Regex("^[A-Z][A-Za-z0-9]*_System\\d{2}$", RegexOptions.Compiled);
        private readonly NamingCodeRegistryService _codeRegistry;
        private readonly SystemRegistryService _systemRegistry;
        private readonly SpaceZoneService _spaceZoneService;
        private readonly IfcDefaultsResolverService _ifcDefaults;
        private readonly SystemCatalogService _systemCatalog = new SystemCatalogService();
        private readonly ParameterWriteService _parameterWriter = new ParameterWriteService();


        private class TypeDescriptor
        {
            public string Category { get; set; }
            public string Family { get; set; }
            public string Type { get; set; }
            public string IfcClass { get; set; }
            public string PredefinedDisplay { get; set; }
            public string PredefinedSchema { get; set; }
            public string UserDefined { get; set; }
        }

        public AuthoringNamingService(NamingCodeRegistryService codeRegistry, SystemRegistryService systemRegistry, SpaceZoneService spaceZoneService, IfcDefaultsResolverService ifcDefaults)
        {
            _codeRegistry = codeRegistry;
            _systemRegistry = systemRegistry;
            _spaceZoneService = spaceZoneService;
            _ifcDefaults = ifcDefaults;
        }

        public NamingPreviewResult GeneratePreview(Document doc, NamingGenerationRequest request)
        {
            var result = new NamingPreviewResult();
            var elements = ResolveScope(doc, request);
            result.SelectedCount = elements.Count;
            var sorted = elements
                .OrderBy(e => e.Category?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e =>
                {
                    var t = doc.GetElement(e.GetTypeId()) as ElementType;
                    return t?.FamilyName ?? string.Empty;
                }, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e =>
                {
                    var t = doc.GetElement(e.GetTypeId()) as ElementType;
                    return t?.Name ?? string.Empty;
                }, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.GetTypeId().Value)
                .ThenBy(e => e.Id.Value)
                .ToList();

            var typeCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var typeNameByTypeId = new Dictionary<long, string>();
            var instanceCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var doorRoomCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var windowRoomCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var systemCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var typeDescriptors = new Dictionary<long, TypeDescriptor>();

            foreach (var element in sorted)
            {
                var typeElement = doc.GetElement(element.GetTypeId()) as ElementType;
                var typeId = typeElement?.Id?.Value ?? -1;
                if (typeId <= 0 || typeDescriptors.ContainsKey(typeId))
                {
                    continue;
                }

                var category = element.Category?.Name ?? string.Empty;
                var family = typeElement?.FamilyName ?? string.Empty;
                var type = typeElement?.Name ?? string.Empty;
                var resolved = _ifcDefaults.ResolveDefaults(category, family, type);
                var ifcClass = NormalizeIfcClass(typeElement, category, resolved.Entity);
                var predefinedRaw = Get(typeElement, "IFC Predefined Type", "DfE_IFCPredefinedType", "IFC_Predefined_Type");
                var resolvedRaw = string.IsNullOrWhiteSpace(predefinedRaw) ? resolved.PredefinedType : predefinedRaw;
                var predefinedSchema = NormalizeSchemaToken(resolvedRaw, request.FallbackPredefinedType);
                var predefinedDisplay = ToPascalCase(predefinedSchema);
                var userDefined = predefinedSchema == "USERDEFINED" ? (resolved.UserDefinedValue ?? string.Empty) : string.Empty;
                typeDescriptors[typeId] = new TypeDescriptor
                {
                    Category = category,
                    Family = family,
                    Type = type,
                    IfcClass = ifcClass,
                    PredefinedDisplay = predefinedDisplay,
                    PredefinedSchema = predefinedSchema,
                    UserDefined = userDefined
                };
            }

            foreach (var descriptor in typeDescriptors
                .OrderBy(x => x.Value.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Value.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Value.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key))
            {
                var typeId = descriptor.Key;
                var value = descriptor.Value;
                var bucket = $"{value.Category}_{value.IfcClass}_{value.PredefinedDisplay}";
                if (!typeCounter.ContainsKey(bucket))
                {
                    typeCounter[bucket] = 0;
                }

                typeCounter[bucket]++;
                var typeSuffix = typeCounter[bucket].ToString().PadLeft(request.TypeNumberWidth, '0');
                var proposedTypeName = string.IsNullOrWhiteSpace(value.PredefinedDisplay) || value.PredefinedDisplay.Equals("Notdefined", StringComparison.OrdinalIgnoreCase)
                    ? Sanitize($"{value.IfcClass}_Type{typeSuffix}")
                    : Sanitize($"{value.IfcClass}_{value.PredefinedDisplay}_Type{typeSuffix}");
                typeNameByTypeId[typeId] = proposedTypeName;
            }

            foreach (var element in sorted)
            {
                var row = new NamingPreviewRow
                {
                    ElementId = element.Id.Value,
                    Category = element.Category?.Name ?? string.Empty,
                    Level = doc.GetElement(element.LevelId)?.Name ?? string.Empty,
                    CurrentIfcName = Get(element, "IFCName", "IfcName"),
                    CurrentSystemName = Get(element, "SystemName")
                };

                var typeElement = doc.GetElement(element.GetTypeId()) as ElementType;
                row.TypeElementId = typeElement?.Id?.Value ?? -1;
                row.Family = typeElement?.FamilyName ?? string.Empty;
                row.Type = typeElement?.Name ?? string.Empty;
                row.CurrentIfcTypeName = Get(typeElement, "IFCName [Type]", "IFCName[Type]");

                var resolved = _ifcDefaults.ResolveDefaults(row.Category, row.Family, row.Type);
                var descriptor = typeDescriptors.TryGetValue(row.TypeElementId, out var cached)
                    ? cached
                    : new TypeDescriptor
                    {
                        Category = row.Category,
                        Family = row.Family,
                        Type = row.Type,
                        IfcClass = NormalizeIfcClass(typeElement, row.Category, resolved.Entity),
                        PredefinedDisplay = "Notdefined",
                        PredefinedSchema = "NOTDEFINED",
                        UserDefined = string.Empty
                    };
                var ifcClass = descriptor.IfcClass;
                var predefined = descriptor.PredefinedDisplay;
                var predefinedSchema = descriptor.PredefinedSchema;
                var proposedTypeName = typeNameByTypeId.TryGetValue(row.TypeElementId, out var assigned) ? assigned : string.Empty;
                row.ProposedIfcTypeName = proposedTypeName;
                row.ProposedIfcExportAs = ifcClass;
                row.ProposedIfcEntity = ifcClass;
                row.ProposedIfcPredefinedType = predefinedSchema;
                row.ProposedUserDefinedPredefinedType = descriptor.UserDefined;

                row.ProposedIfcName = BuildInstanceName(doc, element, ifcClass, predefined, request, instanceCounter, doorRoomCounter, windowRoomCounter, out var status);
                row.Status = status;
                row.Eligible = status == "OK" || status.StartsWith("WARN");

                row.SourceSsNumber = Get(typeElement, "Classification.Uniclass.Ss.Number");
                row.SourceSsDescription = Get(typeElement, "Classification.Uniclass.Ss.Description");
                if (string.IsNullOrWhiteSpace(row.SourceSsNumber))
                {
                    row.SourceSsNumber = Get(element, "Classification.Uniclass.Ss.Number");
                    row.SourceSsDescription = Get(element, "Classification.Uniclass.Ss.Description");
                }

                var selectedSystem = _systemRegistry.Find(request.SelectedSystemName);
                var candidates = _systemCatalog.ResolveCandidatesByClassification(row.SourceSsNumber);
                var resolvedSystem = selectedSystem ?? candidates.FirstOrDefault();
                var systemBase = resolvedSystem?.SystemName ?? request.SelectedSystemName ?? "USERDEFINED";
                row.MatchedSystemPrefix = resolvedSystem?.MatchedPrefix ?? string.Empty;
                row.IsUserDefinedSystem = string.Equals(systemBase, "USERDEFINED", StringComparison.OrdinalIgnoreCase);
                row.CandidateSystems = string.Join(" | ", candidates.Select(c => c.SystemName));
                row.ProposedSystemName = BuildSystemName(systemBase, row.Category, request, systemCounter);
                row.ProposedSystemDescription = resolvedSystem?.SystemDescription ?? (row.IsUserDefinedSystem ? "User defined system." : (string.IsNullOrWhiteSpace(systemBase) ? string.Empty : $"Generated for {systemBase}"));
                row.ProposedSystemCategory = string.IsNullOrWhiteSpace(row.SourceSsNumber) ? string.Empty : $"{row.SourceSsNumber} : {row.SourceSsDescription}".Trim().TrimEnd(':').Trim();

                if (resolvedSystem != null && !_systemRegistry.IsCompatible(resolvedSystem, row.Category, ifcClass))
                {
                    row.Status = AppendStatus(row.Status, "WARN: system may be incompatible");
                }

                result.Rows.Add(row);
            }

            result.EligibleCount = result.Rows.Count(r => r.Eligible);
            result.SkippedCount = result.Rows.Count(r => !r.Eligible);
            result.ErrorCount = result.Rows.Count(r => r.Status.Contains("ERR"));
            result.ResolvedIfcEntityCount = result.Rows.Count(r => !string.IsNullOrWhiteSpace(r.ProposedIfcEntity));
            result.ResolvedPredefinedTypeCount = result.Rows.Count(r => !string.IsNullOrWhiteSpace(r.ProposedIfcPredefinedType));
            result.UserDefinedFallbackCount = result.Rows.Count(r => string.Equals(r.ProposedIfcPredefinedType, "USERDEFINED", StringComparison.OrdinalIgnoreCase));
            result.UnresolvedCount = result.Rows.Count(r => string.IsNullOrWhiteSpace(r.ProposedIfcEntity) || string.IsNullOrWhiteSpace(r.ProposedIfcPredefinedType));

            var duplicates = result.Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ProposedIfcName))
                .GroupBy(r => r.ProposedIfcName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            foreach (var duplicate in duplicates)
            {
                result.Warnings.Add("Duplicate proposed IFCName: " + duplicate);
            }
            var distinctTypeRows = result.Rows.Where(r => r.TypeElementId > 0).GroupBy(r => r.TypeElementId).ToList();
            var reusedTypeNameCount = distinctTypeRows.Count(g => g.Select(x => x.ProposedIfcTypeName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && g.Count() > 1);
            result.Warnings.Add($"Type numbering diagnostics: distinct types={distinctTypeRows.Count}, unique IFCName[Type] values={distinctTypeRows.Select(g => g.First().ProposedIfcTypeName).Distinct(StringComparer.OrdinalIgnoreCase).Count()}, repeated instances reusing same type name={reusedTypeNameCount}.");
            var catalogError = _systemCatalog.GetLastError();
            if (!string.IsNullOrWhiteSpace(catalogError))
            {
                result.Warnings.Add("System catalog load warning: " + catalogError);
            }

            return result;
        }

        public ApplyResult Apply(Document doc, IEnumerable<NamingPreviewRow> rows, bool applyInstance, bool applyType, bool applySystem)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Apply Naming/System Data"))
            {
                tx.Start();
                var typeWriteSet = new HashSet<long>();
                foreach (var row in rows ?? Enumerable.Empty<NamingPreviewRow>())
                {
                    try
                    {
                        if (!row.Eligible)
                        {
                            result.Skipped++;
                            result.Logs.Add($"Scope=Instance; Target={row.ElementId}; Parameter=n/a; Status=Skipped; Reason=row not eligible");
                            continue;
                        }

                        var element = doc.GetElement(new ElementId(row.ElementId));
                        if (element == null)
                        {
                            result.Failed++;
                            result.Logs.Add($"Scope=Instance; Target={row.ElementId}; Parameter=n/a; Status=Failed; Reason=missing element");
                            continue;
                        }

                        if (applyInstance)
                        {
                            if (_parameterWriter.SetInstanceParameter(element, "IFCName", row.ProposedIfcName, result) ||
                                _parameterWriter.SetInstanceParameter(element, "IfcName", row.ProposedIfcName, result))
                            {
                                result.InstancesUpdated++;
                            }
                        }

                        if (applySystem)
                        {
                            if (row.IsUserDefinedSystem && !string.IsNullOrWhiteSpace(row.UserDefinedValidationError))
                            {
                                result.Skipped++;
                                result.Logs.Add($"Scope=Instance; Target={row.ElementId}; Parameter=SystemName; Status=Skipped; Reason={row.UserDefinedValidationError}");
                            }
                            else
                            {
                                _parameterWriter.SetInstanceParameter(element, "SystemName", row.ProposedSystemName, result);
                                _parameterWriter.SetInstanceParameter(element, "SystemDescription", row.ProposedSystemDescription, result);
                                _parameterWriter.SetInstanceParameter(element, "SystemCategory", string.IsNullOrWhiteSpace(row.ProposedSystemCategory) ? row.Category : row.ProposedSystemCategory, result);
                            }
                        }

                        if (applyType)
                        {
                            var type = doc.GetElement(element.GetTypeId());
                            var typeId = type?.Id?.Value ?? -1;
                            if (typeId <= 0)
                            {
                                result.Skipped++;
                                result.Logs.Add($"Scope=Type; Target={row.ElementId}; Parameter=n/a; Status=Skipped; Reason=missing valid type");
                                continue;
                            }

                            if (typeWriteSet.Add(typeId))
                            {
                                _parameterWriter.SetTypeParameter(doc, element, "IFCName [Type]", row.ProposedIfcTypeName, result);
                                _parameterWriter.SetTypeParameter(doc, element, "IFCName[Type]", row.ProposedIfcTypeName, result);
                                _parameterWriter.SetTypeParameter(doc, element, "IfcName[Type]", row.ProposedIfcTypeName, result);
                                if (TryWriteIfcExportAs(doc, element, row, result))
                                {
                                    result.ExportAsUpdated++;
                                }
                                _parameterWriter.SetTypeParameter(doc, element, "IFC Predefined Type", row.ProposedIfcPredefinedType, result);
                                _parameterWriter.SetTypeParameter(doc, element, "DfE_IFCPredefinedType", row.ProposedIfcPredefinedType, result);
                                _parameterWriter.SetTypeParameter(doc, element, "DfE_IFCEntity", row.ProposedIfcEntity, result);
                                _parameterWriter.SetTypeParameter(doc, element, "DfE_UserDefinedPredefinedTypeValue", row.ProposedUserDefinedPredefinedType, result);
                                result.UniqueTypesUpdated++;
                            }
                        }

                        result.Updated++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Logs.Add($"Scope=Instance; Target={row.ElementId}; Parameter=n/a; Status=Failed; Reason={ex.Message}");
                    }
                }

                tx.Commit();
            }

            return result;
        }

        private bool TryWriteIfcExportAs(Document doc, Element element, NamingPreviewRow row, ApplyResult result)
        {
            if (string.IsNullOrWhiteSpace(row?.ProposedIfcEntity))
            {
                result.Skipped++;
                result.Logs.Add($"Scope=Type; Target={row?.ElementId}; Parameter=Export to IFC As; Status=Skipped; Reason=unresolved IFC entity");
                return false;
            }

            var exportValue = row.ProposedIfcEntity.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)
                ? row.ProposedIfcEntity
                : "Ifc" + row.ProposedIfcEntity;
            return _parameterWriter.SetTypeParameter(doc, element, "Export to IFC As", exportValue, result)
                || _parameterWriter.SetTypeParameter(doc, element, "IFC Export As", exportValue, result)
                || _parameterWriter.SetTypeParameter(doc, element, "IfcExportAs", exportValue, result);
        }

        private string BuildInstanceName(Document doc, Element element, string ifcClass, string predefined, NamingGenerationRequest req,
            IDictionary<string, int> instanceCounter, IDictionary<string, int> doorCounter, IDictionary<string, int> windowCounter, out string status)
        {
            status = "OK";
            var category = element.Category?.Name ?? string.Empty;
            if (category.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 || ifcClass.Equals("Door", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDoorWindowName(doc, element, "D", req, doorCounter, out status);
            }

            if (category.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 || ifcClass.Equals("Window", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDoorWindowName(doc, element, "W", req, windowCounter, out status);
            }

            var code = _codeRegistry.ResolveCode(ifcClass, predefined);
            if (string.IsNullOrWhiteSpace(code))
            {
                if (!req.UseFallbackCode)
                {
                    status = "ERR: missing code mapping";
                    return string.Empty;
                }

                code = req.FallbackCode;
                status = "WARN: fallback code used";
            }

            code = Sanitize(code);
            if (req.InstanceNumberingMode == InstanceNumberingMode.ElementId)
            {
                return code + element.Id.Value;
            }

            var counterKey = $"{category}_{ifcClass}";
            if (!instanceCounter.ContainsKey(counterKey)) instanceCounter[counterKey] = 0;
            instanceCounter[counterKey]++;
            return code + instanceCounter[counterKey].ToString("D4");
        }

        private string BuildDoorWindowName(Document doc, Element element, string prefix, NamingGenerationRequest req, IDictionary<string, int> counter, out string status)
        {
            var room = _spaceZoneService.ResolveRoom(doc, element);
            var roomNumber = room?.Number;
            if (string.IsNullOrWhiteSpace(roomNumber))
            {
                if (!req.AllowDoorWindowUnassignedFallback)
                {
                    status = "ERR: missing room number";
                    return string.Empty;
                }

                roomNumber = req.UnassignedRoomPrefix;
                status = "WARN: unassigned room fallback used";
            }
            else
            {
                status = "OK";
            }

            if (!counter.ContainsKey(roomNumber)) counter[roomNumber] = 0;
            counter[roomNumber]++;
            return SanitizeKeepingDots($"{roomNumber?.Trim()}-{prefix}{counter[roomNumber].ToString("D2")}");
        }

        private static IList<Element> ResolveScope(Document doc, NamingGenerationRequest request)
        {
            var selected = new HashSet<long>(request?.CategoryIds ?? new List<long>());
            if (request.ScopeMode == NamingScopeMode.CurrentSelection || request.ScopeMode == NamingScopeMode.AllInstancesOfSelectedTypes)
            {
                var ids = new Autodesk.Revit.UI.UIDocument(doc).Selection.GetElementIds();
                var selectedElements = ids.Select(id => doc.GetElement(id)).Where(e => e != null && e.Category != null).ToList();
                if (request.ScopeMode == NamingScopeMode.CurrentSelection)
                {
                    return selectedElements;
                }

                var typeIds = selectedElements.Select(e => e.GetTypeId()).ToHashSet();
                return new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(e => e.Category != null && typeIds.Contains(e.GetTypeId()))
                    .ToList();
            }

            var collector = request.ScopeMode == NamingScopeMode.CurrentView
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            var all = collector.WhereElementIsNotElementType().Where(e => e.Category != null).ToList();
            if (request.ScopeMode == NamingScopeMode.SelectedCategories || request.ScopeMode == NamingScopeMode.WholeModelByCategory)
            {
                if (selected.Count > 0)
                {
                    return all.Where(e => selected.Contains(e.Category.Id.Value)).ToList();
                }
            }

            return all;
        }

        private static string Get(Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var p = element?.LookupParameter(name);
                if (p != null) return p.AsString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static void Set(Element element, string value, params string[] names)
        {
            foreach (var name in names)
            {
                var p = element?.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(value ?? string.Empty);
                    return;
                }
            }
        }

        private static string NormalizeIfcClass(ElementType type, string fallbackCategory, string resolverSuggestion)
        {
            var exportAs = Get(type, "IfcExportAs", "IFC Export As");
            if (!string.IsNullOrWhiteSpace(exportAs))
            {
                return NormalizeToken(exportAs.Replace("Ifc", string.Empty), "Undefined");
            }

            if (!string.IsNullOrWhiteSpace(resolverSuggestion))
            {
                return NormalizeToken(resolverSuggestion, "Undefined");
            }

            return NormalizeToken(fallbackCategory?.Replace(" ", string.Empty), "Undefined");
        }


        private static string NormalizeSchemaToken(string token, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(token) ? fallback : token;
            return NormalizeToken(raw, fallback).ToUpperInvariant();
        }

        private static string ToPascalCase(string schemaToken)
        {
            if (string.IsNullOrWhiteSpace(schemaToken)) return string.Empty;
            var parts = schemaToken.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p => p.Substring(0, 1).ToUpperInvariant() + p.Substring(1).ToLowerInvariant()));
        }
        private static string NormalizeToken(string token, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(token) ? fallback : token;
            return Regex.Replace(raw ?? fallback, "[^A-Za-z0-9_]", string.Empty);
        }

        private static string Sanitize(string value) => InvalidIfcNameCharacters.Replace(value ?? string.Empty, string.Empty);
        private static string SanitizeKeepingDots(string value) => InvalidIfcNameCharactersAllowDots.Replace(value ?? string.Empty, string.Empty);

        private static string AppendStatus(string current, string add)
        {
            if (string.IsNullOrWhiteSpace(current)) return add;
            if (current.Contains(add)) return current;
            return current + "; " + add;
        }

        private static string BuildSystemName(string baseName, string category, NamingGenerationRequest request, IDictionary<string, int> systemCounter)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return string.Empty;
            if (!request.AddAsNewSystem && request.AppendToExistingSystem) return baseName;

            var normalizedBase = Sanitize(baseName.Replace(" ", "_"));
            var key = $"{normalizedBase}::{category}";
            if (!systemCounter.ContainsKey(key)) systemCounter[key] = 0;
            systemCounter[key]++;
            return $"{normalizedBase}_System{systemCounter[key]:00}";
        }

        public static string ValidateUserDefinedSystemName(string userDefinedSystemName)
        {
            return UserDefinedSystemNameRegex.IsMatch(userDefinedSystemName ?? string.Empty)
                ? string.Empty
                : "Invalid user-defined name. Expected PascalCaseName_SystemXX.";
        }
    }
}
