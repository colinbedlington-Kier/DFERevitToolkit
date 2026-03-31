using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class DfeComplianceValidationService
    {
        private readonly AuthoringNamingService _namingService;
        private readonly SpaceZoneService _spaceZoneService;
        private readonly ClassificationSyncService _classificationSyncService;
        private readonly IfcDefaultsResolverService _ifcDefaultsResolver;

        public DfeComplianceValidationService(
            AuthoringNamingService namingService,
            SpaceZoneService spaceZoneService,
            ClassificationSyncService classificationSyncService,
            IfcDefaultsResolverService ifcDefaultsResolver)
        {
            _namingService = namingService;
            _spaceZoneService = spaceZoneService;
            _classificationSyncService = classificationSyncService;
            _ifcDefaultsResolver = ifcDefaultsResolver;
        }

        public ComplianceRunSummary BuildComplianceSummary(Document doc, IList<long> categoryIds = null)
        {
            var request = new NamingGenerationRequest
            {
                ScopeMode = NamingScopeMode.WholeModelByCategory,
                CategoryIds = categoryIds?.ToList() ?? new List<long>(),
                UseFallbackCode = true,
                FallbackCode = "UNM",
                InstanceNumberingMode = InstanceNumberingMode.ElementId,
                AllowDoorWindowUnassignedFallback = true
            };

            var naming = _namingService.GeneratePreview(doc, request);
            var classification = _classificationSyncService.BuildPreview(doc, categoryIds ?? new List<long>());
            var roomAssignments = _spaceZoneService.BuildPreview(doc, new SpaceZoneRequest());
            var roomById = roomAssignments.Rows.ToDictionary(x => x.ElementId, x => x);
            var classificationByElement = classification.Rows.ToDictionary(x => x.ElementId, x => x);

            var rows = new List<ComplianceCheckResult>();
            foreach (var row in naming.Rows)
            {
                var typeElement = doc.GetElement(new ElementId(row.TypeElementId)) as ElementType;
                var currentIfcEntity = Get(typeElement, "Export to IFC As", "IFC Export As", "IfcExportAs");
                var currentIfcPredefined = Get(typeElement, "IFC Predefined Type", "DfE_IFCPredefinedType", "IFC_Predefined_Type");
                var currentAds = Get(typeElement, "DfE ADS Classification") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(currentAds))
                {
                    var element = doc.GetElement(new ElementId(row.ElementId));
                    currentAds = Get(element, "DfE ADS Classification");
                }

                var zone = roomById.TryGetValue(row.ElementId, out var room) ? room.CurrentZoneName : string.Empty;
                var level = row.Level;

                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "Naming", "IFC-NAME", "IFC naming matches DfE rule",
                    "IFCName should match the generated naming value for the DfE schema.",
                    row.CurrentIfcName, row.ProposedIfcName,
                    highlightIfcName: true));

                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "Naming", "IFC-TYPE-NAME", "IFC type naming matches DfE rule",
                    "IFCName[Type] should match the generated type naming value for the DfE schema.",
                    row.CurrentIfcTypeName, row.ProposedIfcTypeName,
                    highlightIfcTypeName: true));

                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "IFC Mapping", "IFC-ENTITY", "IFC entity is valid for rule context",
                    "IFC entity should match the DfE default mapping from existing IFC resolver/catalog.",
                    NormalizeIfcEntity(currentIfcEntity), NormalizeIfcEntity(row.ProposedIfcEntity),
                    highlightIfcEntity: true));

                var expectedPredefined = string.IsNullOrWhiteSpace(row.ProposedIfcPredefinedType)
                    ? _ifcDefaultsResolver.ResolveDefaults(row.Category, row.Family, row.Type).PredefinedType
                    : row.ProposedIfcPredefinedType;
                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "IFC Mapping", "IFC-PREDEFINED", "IFC predefined type is valid",
                    "IFC predefined type should match the mapped DfE IFC predefined type.",
                    (currentIfcPredefined ?? string.Empty).ToUpperInvariant(), (expectedPredefined ?? string.Empty).ToUpperInvariant(),
                    highlightIfcPredefinedType: true));

                var classRow = classificationByElement.TryGetValue(row.ElementId, out var cRow) ? cRow : null;
                var expectedClassification = classRow?.ProposedClassification3;
                var actualClassification = !string.IsNullOrWhiteSpace(currentAds) ? currentAds : (classRow?.SourceSsNumber ?? string.Empty);
                var classificationApplicable = !string.IsNullOrWhiteSpace(expectedClassification) || !string.IsNullOrWhiteSpace(actualClassification);
                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "Classification", "ADS-CLASS", "ADS/classification exists",
                    "DfE ADS/classification value should be populated from existing classification sources.",
                    actualClassification, expectedClassification,
                    isApplicable: classificationApplicable,
                    highlightAdsClassification: true));

                var hasSystemContext = !string.IsNullOrWhiteSpace(row.SourceSsNumber) ||
                                       row.Category.IndexOf("duct", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       row.Category.IndexOf("pipe", StringComparison.OrdinalIgnoreCase) >= 0;
                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "System", "SYSTEM-NAME", "System naming/assignment present",
                    "SystemName should be populated where system assignment is relevant.",
                    row.CurrentSystemName, string.IsNullOrWhiteSpace(row.CurrentSystemName) ? "<required>" : row.CurrentSystemName,
                    isApplicable: hasSystemContext,
                    highlightSystemName: true));

                var roomZoneApplicable = row.Category.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0;
                rows.Add(BuildCheck(row, level, currentIfcEntity, currentIfcPredefined, currentAds, zone,
                    "Room/Zone", "ZONE-ASSIGNMENT", "Zone/space assignment present",
                    "ZoneName/SpaceReference should be assigned for room/space context.",
                    zone, string.IsNullOrWhiteSpace(zone) ? "<required>" : zone,
                    isApplicable: roomZoneApplicable,
                    highlightZoneName: true));
            }

            var manifest = ParameterBindingManifest.All().Where(x => x.RequiredForSetupCheck).ToList();
            var sampleInstance = new FilteredElementCollector(doc).WhereElementIsNotElementType().FirstOrDefault();
            var sampleType = sampleInstance == null ? null : doc.GetElement(sampleInstance.GetTypeId());
            foreach (var rule in manifest)
            {
                var exists = rule.Scope == ParameterScopeKind.Project
                    ? doc.ProjectInformation.LookupParameter(rule.Name) != null
                    : rule.Scope == ParameterScopeKind.Type
                        ? sampleType?.LookupParameter(rule.Name) != null
                        : sampleInstance?.LookupParameter(rule.Name) != null;
                rows.Add(new ComplianceCheckResult
                {
                    ElementId = 0,
                    Category = "<schema>",
                    Family = string.Empty,
                    Type = string.Empty,
                    Level = string.Empty,
                    RuleGroup = "Parameter Schema",
                    RuleId = "PARAM-" + rule.Name,
                    RuleName = "Required parameter exists and scope is valid",
                    ExpectedRequirement = $"{rule.Name} ({rule.Scope}) is required by DfE manifest.",
                    ActualValue = exists ? $"Present ({rule.Scope})" : "Missing",
                    Status = exists ? "Pass" : "Fail",
                    Severity = "Error",
                    Notes = exists ? "Verified from existing parameter manifest." : "Missing parameter from required manifest.",
                    IsApplicable = true,
                    IsSelected = true
                });
            }

            var applicable = rows.Where(x => x.IsApplicable).ToList();
            var failed = applicable.Where(x => x.IsFailed).ToList();
            var elementIds = naming.Rows.Select(x => x.ElementId).Distinct().ToList();
            var nonCompliantElementIds = failed.Where(x => x.ElementId > 0).Select(x => x.ElementId).Distinct().ToList();

            return new ComplianceRunSummary
            {
                Rows = rows,
                TotalElementsChecked = elementIds.Count,
                CompliantElementsCount = Math.Max(0, elementIds.Count - nonCompliantElementIds.Count),
                NonCompliantElementsCount = nonCompliantElementIds.Count,
                TotalApplicableChecks = applicable.Count,
                PassedApplicableChecks = applicable.Count(x => x.IsCompliant),
                FailedApplicableChecks = failed.Count,
                ElementCompliancePercent = elementIds.Count == 0 ? 0 : ((double)(elementIds.Count - nonCompliantElementIds.Count) / elementIds.Count) * 100d,
                RuleCompliancePercent = applicable.Count == 0 ? 0 : ((double)applicable.Count(x => x.IsCompliant) / applicable.Count) * 100d,
                MetricDefinition = "Element Compliance % = compliant elements / total checked elements; Rule Compliance % = passed applicable checks / total applicable checks (N/A checks excluded).",
                RuleGroups = rows.Select(x => x.RuleGroup).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
            };
        }

        public ApplyResult OpenCompliance3dView(UIApplication app, IEnumerable<long> elementIds)
        {
            var result = new ApplyResult();
            var uidoc = app?.ActiveUIDocument;
            var doc = uidoc?.Document;
            var ids = (elementIds ?? Enumerable.Empty<long>()).Distinct().Where(x => x > 0).Select(x => new ElementId(x)).ToList();
            if (doc == null || ids.Count == 0)
            {
                result.Logs.Add("No elements selected for 3D compliance review.");
                return result;
            }

            using (var tx = new Transaction(doc, "DfE Compliance Review 3D View"))
            {
                tx.Start();
                var view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, "DfE_Compliance_Review", StringComparison.OrdinalIgnoreCase));

                if (view == null)
                {
                    var vft = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                    if (vft == null)
                    {
                        result.Logs.Add("No 3D view family type found.");
                        tx.RollBack();
                        return result;
                    }

                    view = View3D.CreateIsometric(doc, vft.Id);
                    view.Name = "DfE_Compliance_Review";
                }

                view.IsolateElementsTemporary(ids);
                tx.Commit();

                uidoc.ActiveView = view;
                uidoc.Selection.SetElementIds(ids);
            }

            result.Updated = ids.Count;
            result.Logs.Add($"Isolated {ids.Count} non-compliant element(s) in DfE_Compliance_Review.");
            return result;
        }

        private static ComplianceCheckResult BuildCheck(
            NamingPreviewRow source,
            string level,
            string currentIfcEntity,
            string currentIfcPredefined,
            string currentAds,
            string zone,
            string ruleGroup,
            string ruleId,
            string ruleName,
            string expectedText,
            string actualValue,
            string expectedValue,
            bool isApplicable = true,
            bool highlightIfcName = false,
            bool highlightIfcTypeName = false,
            bool highlightIfcEntity = false,
            bool highlightIfcPredefinedType = false,
            bool highlightAdsClassification = false,
            bool highlightSystemName = false,
            bool highlightZoneName = false)
        {
            if (!isApplicable)
            {
                return new ComplianceCheckResult
                {
                    ElementId = source.ElementId,
                    Category = source.Category,
                    Family = source.Family,
                    Type = source.Type,
                    Level = level,
                    CurrentIfcName = source.CurrentIfcName,
                    CurrentIfcTypeName = source.CurrentIfcTypeName,
                    CurrentIfcEntity = currentIfcEntity,
                    CurrentIfcPredefinedType = currentIfcPredefined,
                    CurrentAdsClassification = currentAds,
                    CurrentSystemName = source.CurrentSystemName,
                    CurrentZoneName = zone,
                    RuleGroup = ruleGroup,
                    RuleId = ruleId,
                    RuleName = ruleName,
                    ExpectedRequirement = expectedText,
                    ActualValue = "N/A",
                    Status = "N/A",
                    Severity = "Info",
                    Notes = "Rule not applicable for this category/element context.",
                    IsApplicable = false,
                    IsSelected = true
                };
            }

            var actual = actualValue ?? string.Empty;
            var expected = expectedValue ?? string.Empty;
            var isPass = !string.IsNullOrWhiteSpace(actual) && string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
            return new ComplianceCheckResult
            {
                ElementId = source.ElementId,
                Category = source.Category,
                Family = source.Family,
                Type = source.Type,
                Level = level,
                CurrentIfcName = source.CurrentIfcName,
                CurrentIfcTypeName = source.CurrentIfcTypeName,
                CurrentIfcEntity = currentIfcEntity,
                CurrentIfcPredefinedType = currentIfcPredefined,
                CurrentAdsClassification = currentAds,
                CurrentSystemName = source.CurrentSystemName,
                CurrentZoneName = zone,
                RuleGroup = ruleGroup,
                RuleId = ruleId,
                RuleName = ruleName,
                ExpectedRequirement = expectedText + " Expected: " + (string.IsNullOrWhiteSpace(expected) ? "<value required>" : expected),
                ActualValue = string.IsNullOrWhiteSpace(actual) ? "<empty>" : actual,
                Status = isPass ? "Pass" : "Fail",
                Severity = isPass ? "Info" : "Error",
                Notes = isPass ? "Compliant." : "Non-compliant value.",
                IsApplicable = true,
                IsSelected = true,
                HighlightIfcName = highlightIfcName && !isPass,
                HighlightIfcTypeName = highlightIfcTypeName && !isPass,
                HighlightIfcEntity = highlightIfcEntity && !isPass,
                HighlightIfcPredefinedType = highlightIfcPredefinedType && !isPass,
                HighlightAdsClassification = highlightAdsClassification && !isPass,
                HighlightSystemName = highlightSystemName && !isPass,
                HighlightZoneName = highlightZoneName && !isPass
            };
        }

        private static string NormalizeIfcEntity(string value)
        {
            var raw = value ?? string.Empty;
            if (raw.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(3);
            }

            return raw.Trim();
        }

        private static string Get(Element element, params string[] names)
        {
            foreach (var name in names ?? Array.Empty<string>())
            {
                var val = element?.LookupParameter(name)?.AsString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            return string.Empty;
        }
    }
}
