using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class IfcAuthoringService
    {
        private readonly CobieSyncService _cobieSync;
        private readonly TemplateConfigService _templateConfig;
        private readonly NamingCodeRegistryService _codeRegistry;
        private readonly SystemRegistryService _systemRegistry;
        private readonly AuthoringNamingService _naming;
        private readonly IfcHeaderService _header;
        private readonly SpaceZoneService _spaceZone;
        private readonly ValidationService _validation;
        private readonly DiagnosticsCollectorService _diagnostics;
        private readonly AuthoringParameterSetupService _setupService;
        private readonly ClassificationSyncService _classificationSync;
        private readonly IfcDefaultsResolverService _ifcDefaults;
        private readonly DfeComplianceValidationService _compliance;

        public IfcAuthoringService(
            CobieSyncService cobieSync,
            TemplateConfigService templateConfig,
            NamingCodeRegistryService codeRegistry,
            SystemRegistryService systemRegistry,
            AuthoringNamingService naming,
            IfcHeaderService header,
            SpaceZoneService spaceZone,
            ValidationService validation,
            DiagnosticsCollectorService diagnostics,
            AuthoringParameterSetupService setupService,
            ClassificationSyncService classificationSync,
            IfcDefaultsResolverService ifcDefaults,
            DfeComplianceValidationService compliance)
        {
            _cobieSync = cobieSync;
            _templateConfig = templateConfig;
            _codeRegistry = codeRegistry;
            _systemRegistry = systemRegistry;
            _naming = naming;
            _header = header;
            _spaceZone = spaceZone;
            _validation = validation;
            _diagnostics = diagnostics;
            _setupService = setupService;
            _classificationSync = classificationSync;
            _ifcDefaults = ifcDefaults;
            _compliance = compliance;

            _codeRegistry.SetEntries(_templateConfig.LoadEmbeddedNamingCodes());
            _systemRegistry.SetEntries(_templateConfig.LoadEmbeddedSystems());
        }

        public SetupCheckResult CheckSetup(Document doc, IList<ElementId> categoryIds)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Checking setup state.");
            var result = _setupService.Check(doc, categoryIds);
            result.NamingMapLoaded = _codeRegistry.GetEntries().Any();
            result.SystemListLoaded = _systemRegistry.GetEntries().Any();
            result.NamingCodesSource = _templateConfig.LastNamingCodesSource;
            result.SystemsSource = _templateConfig.LastSystemsSource;
            result.Notes += $" Naming codes: {_codeRegistry.GetEntries().Count}. Systems: {_systemRegistry.GetEntries().Count}.";
            return result;
        }

        public SetupCheckResult CreateMissingParameters(Document doc, IList<ElementId> categoryIds)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Creating missing parameters from full shared parameter set.");
            var setup = _setupService.CreateMissing(doc, categoryIds);
            setup.NamingMapLoaded = _codeRegistry.GetEntries().Any();
            setup.SystemListLoaded = _systemRegistry.GetEntries().Any();
            setup.NamingCodesSource = _templateConfig.LastNamingCodesSource;
            setup.SystemsSource = _templateConfig.LastSystemsSource;
            return setup;
        }

        public IList<NamingCodeMapEntry> LoadNamingCodes(string path = null)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Loading naming code map.", new { path = path ?? "embedded" });
            var entries = string.IsNullOrWhiteSpace(path) ? _templateConfig.LoadEmbeddedNamingCodes() : _templateConfig.LoadNamingCodesFromPath(path);
            if (entries.Any()) _codeRegistry.SetEntries(entries);
            return _codeRegistry.GetEntries();
        }

        public IList<SystemRegistryEntry> LoadSystems(string path = null)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Loading system list.", new { path = path ?? "embedded" });
            var entries = string.IsNullOrWhiteSpace(path) ? _templateConfig.LoadEmbeddedSystems() : _templateConfig.LoadSystemsFromPath(path);
            var loadedCount = entries?.Count ?? 0;
            if (entries.Any()) _systemRegistry.SetEntries(entries);
            var bound = _systemRegistry.GetEntries();
            var boundCount = bound.Count;
            _diagnostics.AddInfo("Authoring.Setup", "System list loaded and bound.", new
            {
                loadedCount,
                boundCount,
                filteredOut = Math.Max(0, loadedCount - boundCount)
            });
            return bound;
        }

        public NamingPreviewResult GenerateNamingPreview(Document doc, NamingGenerationRequest request)
        {
            _diagnostics.AddInfo("Authoring.Naming", "Generating naming preview.", new { request.ScopeMode, request.InstanceNumberingMode });
            var preview = _naming.GeneratePreview(doc, request);
            preview.Warnings.Add($"Resolved IFC entities: {preview.ResolvedIfcEntityCount}, predefined types: {preview.ResolvedPredefinedTypeCount}, USERDEFINED fallback: {preview.UserDefinedFallbackCount}, unresolved: {preview.UnresolvedCount}.");
            preview.Warnings.Add($"Predefined catalog source: {_ifcDefaults.PredefinedTypesSource} ({_ifcDefaults.PredefinedTypesSourceDetail}), records: {_ifcDefaults.PredefinedTypesCount}.");
            return preview;
        }

        public ApplyResult ApplyNaming(Document doc, IEnumerable<NamingPreviewRow> rows, bool applyIfcName, bool applyTypeName, bool applySystem)
        {
            _diagnostics.AddInfo("Authoring.Naming", "Applying naming/system data.", new { applyIfcName, applyTypeName, applySystem });
            return _naming.Apply(doc, rows, applyIfcName, applyTypeName, applySystem);
        }

        public HeaderDataModel ReadHeader(Document doc) => _header.Read(doc);
        public HeaderValidationResult ValidateHeader(HeaderDataModel model) => _header.Validate(model);
        public ApplyResult WriteHeader(Document doc, HeaderDataModel model)
        {
            _diagnostics.AddInfo("Authoring.Header", "Writing IFC header data.");
            return _header.Write(doc, model);
        }

        public SpaceZonePreviewResult BuildSpaceZonePreview(Document doc, SpaceZoneRequest request)
        {
            _diagnostics.AddInfo("Authoring.SpaceZone", "Resolving room/space assignments.");
            return _spaceZone.BuildPreview(doc, request);
        }

        public ApplyResult ApplySpaceReference(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            _diagnostics.AddInfo("Authoring.SpaceZone", "Applying SpaceReference values.");
            return _spaceZone.ApplySpaceReference(doc, rows);
        }

        public ApplyResult ApplyZone(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            _diagnostics.AddInfo("Authoring.SpaceZone", "Applying ZoneName/ZoneDescription/ZoneCategory values only.");
            return _spaceZone.ApplyZone(doc, rows);
        }

        public ApplyResult ApplyAds(Document doc, IEnumerable<SpaceZonePreviewRow> rows)
        {
            _diagnostics.AddInfo("Authoring.SpaceZone", "Applying ADS classification/code values only.");
            return _spaceZone.ApplyAds(doc, rows);
        }

        public SyncResult SyncCobie(Document doc)
        {
            _diagnostics.AddInfo("Authoring.COBie", "Running initial COBie sync mappings.");
            return _cobieSync.ApplyInitialCobieMappings(doc, _cobieSync.GetModelCategories(doc).Select(c => c.Id.Value).ToList());
        }

        public ValidationSummary BuildValidationSummary(Document doc, SetupCheckResult setup, NamingPreviewResult naming, HeaderValidationResult header, SpaceZonePreviewResult space)
        {
            return _validation.BuildSummary(doc, setup, naming, header, space);
        }

        public IList<Category> GetCategories(Document doc, IList<ElementId> selected = null) => _cobieSync.GetModelCategories(doc, selected);
        public IList<SystemRegistryEntry> GetSystems() => _systemRegistry.GetEntries();
        public IList<ZoneCatalogEntry> GetZones(Document doc = null) => _spaceZone.GetZones(doc);
        public IList<AdsClassificationEntry> GetAdsClassifications(Document doc = null) => _spaceZone.GetAdsClassifications(doc);
        public ClassificationSyncResult BuildClassificationSyncPreview(Document doc, IList<long> categoryIds) => _classificationSync.BuildPreview(doc, categoryIds);
        public ApplyResult ApplyClassificationSync(Document doc, IEnumerable<ClassificationSyncPreviewRow> rows) => _classificationSync.Apply(doc, rows);
        public IList<string> GetExistingSystemNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Select(e => e.LookupParameter("SystemName")?.AsString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public ComplianceRunSummary BuildComplianceReport(Document doc, IList<long> categoryIds) => _compliance.BuildComplianceSummary(doc, categoryIds);
        public ApplyResult OpenComplianceReview3d(UIApplication app, IEnumerable<long> elementIds) => _compliance.OpenCompliance3dView(app, elementIds);
    }
}
