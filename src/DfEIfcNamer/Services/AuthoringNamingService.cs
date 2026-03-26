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
        private readonly NamingCodeRegistryService _codeRegistry;
        private readonly SystemRegistryService _systemRegistry;
        private readonly SpaceZoneService _spaceZoneService;

        public AuthoringNamingService(NamingCodeRegistryService codeRegistry, SystemRegistryService systemRegistry, SpaceZoneService spaceZoneService)
        {
            _codeRegistry = codeRegistry;
            _systemRegistry = systemRegistry;
            _spaceZoneService = spaceZoneService;
        }

        public NamingPreviewResult GeneratePreview(Document doc, NamingGenerationRequest request)
        {
            var result = new NamingPreviewResult();
            var elements = ResolveScope(doc, request);
            result.SelectedCount = elements.Count;
            var sorted = elements.OrderBy(e => e.Id.Value).ToList();

            var typeCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var instanceCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var doorRoomCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var windowRoomCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
                row.Family = typeElement?.FamilyName ?? string.Empty;
                row.Type = typeElement?.Name ?? string.Empty;
                row.CurrentIfcTypeName = Get(typeElement, "IFCName [Type]", "IFCName[Type]");

                var ifcClass = NormalizeIfcClass(typeElement, row.Category);
                var predefined = NormalizeToken(Get(element, "DfE_IFCPredefinedType", "IFC Predefined Type"), request.FallbackPredefinedType);
                var typeKey = ifcClass + "_" + predefined;
                if (!typeCounter.ContainsKey(typeKey)) typeCounter[typeKey] = 0;
                typeCounter[typeKey]++;
                row.ProposedIfcTypeName = Sanitize($"{ifcClass}_{predefined}_Type{typeCounter[typeKey].ToString().PadLeft(request.TypeNumberWidth, '0')}");

                row.ProposedIfcName = BuildInstanceName(doc, element, ifcClass, predefined, request, instanceCounter, doorRoomCounter, windowRoomCounter, out var status);
                row.Status = status;
                row.Eligible = status == "OK" || status.StartsWith("WARN");

                var selectedSystem = _systemRegistry.Find(request.SelectedSystemName);
                row.ProposedSystemName = selectedSystem?.SystemName ?? string.Empty;
                row.ProposedSystemDescription = selectedSystem?.SystemDescription ?? string.Empty;

                if (selectedSystem != null && !_systemRegistry.IsCompatible(selectedSystem, row.Category, ifcClass))
                {
                    row.Status = AppendStatus(row.Status, "WARN: system may be incompatible");
                }

                result.Rows.Add(row);
            }

            result.EligibleCount = result.Rows.Count(r => r.Eligible);
            result.SkippedCount = result.Rows.Count(r => !r.Eligible);
            result.ErrorCount = result.Rows.Count(r => r.Status.Contains("ERR"));

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

            return result;
        }

        public ApplyResult Apply(Document doc, IEnumerable<NamingPreviewRow> rows, bool applyInstance, bool applyType, bool applySystem)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE Apply Naming/System Data"))
            {
                tx.Start();
                foreach (var row in rows ?? Enumerable.Empty<NamingPreviewRow>())
                {
                    try
                    {
                        if (!row.Eligible)
                        {
                            result.Skipped++;
                            continue;
                        }

                        var element = doc.GetElement(new ElementId(row.ElementId));
                        if (element == null)
                        {
                            result.Failed++;
                            continue;
                        }

                        if (applyInstance)
                        {
                            Set(element, row.ProposedIfcName, "IFCName", "IfcName");
                        }

                        if (applySystem)
                        {
                            Set(element, row.ProposedSystemName, "SystemName");
                            Set(element, row.ProposedSystemDescription, "SystemDescription");
                        }

                        if (applyType)
                        {
                            var type = doc.GetElement(element.GetTypeId());
                            Set(type, row.ProposedIfcTypeName, "IFCName [Type]", "IFCName[Type]");
                        }

                        result.Updated++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Logs.Add($"Element {row.ElementId}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return result;
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

            if (!instanceCounter.ContainsKey(code)) instanceCounter[code] = 0;
            instanceCounter[code]++;
            return code + instanceCounter[code].ToString("D4");
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
            return Sanitize($"{roomNumber}-{prefix}{counter[roomNumber].ToString("D2")}");
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

        private static string NormalizeIfcClass(ElementType type, string fallbackCategory)
        {
            var exportAs = Get(type, "IFC Export As");
            if (!string.IsNullOrWhiteSpace(exportAs))
            {
                return NormalizeToken(exportAs.Replace("Ifc", string.Empty), "Undefined");
            }

            return NormalizeToken(fallbackCategory?.Replace(" ", string.Empty), "Undefined");
        }

        private static string NormalizeToken(string token, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(token) ? fallback : token;
            return Regex.Replace(raw ?? fallback, "[^A-Za-z0-9_]", string.Empty);
        }

        private static string Sanitize(string value) => InvalidIfcNameCharacters.Replace(value ?? string.Empty, string.Empty);

        private static string AppendStatus(string current, string add)
        {
            if (string.IsNullOrWhiteSpace(current)) return add;
            if (current.Contains(add)) return current;
            return current + "; " + add;
        }
    }
}
