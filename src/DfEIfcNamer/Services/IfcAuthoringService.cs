using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class IfcAuthoringService
    {
        private static readonly (string Name, string Scope, bool IsType)[] RequiredParameters =
        {
            ("IFCName", "Instance", false),
            ("IFCDescription", "Instance", false),
            ("SystemName", "Instance", false),
            ("SystemDescription", "Instance", false),
            ("ZoneName", "Instance", false),
            ("SpaceReference", "Instance", false),
            ("IFCName [Type]", "Type", true),
            ("TypeDescription", "Type", true),
            ("IfcProjectName", "Project", false),
            ("IfcProjectDescription", "Project", false),
            ("IfcSiteName", "Project", false),
            ("IfcSiteDescription", "Project", false),
            ("IfcBuildingName", "Project", false),
            ("IfcBuildingDescription", "Project", false),
            ("UPRN", "Project", false),
            ("MaximumBlockHeight", "Project", false)
        };

        private readonly CobieSyncService _cobieSync;
        private readonly TemplateConfigService _templateConfig;
        private readonly NamingCodeRegistryService _codeRegistry;
        private readonly SystemRegistryService _systemRegistry;
        private readonly AuthoringNamingService _naming;
        private readonly IfcHeaderService _header;
        private readonly SpaceZoneService _spaceZone;
        private readonly ValidationService _validation;
        private readonly DiagnosticsCollectorService _diagnostics;

        public IfcAuthoringService(
            CobieSyncService cobieSync,
            TemplateConfigService templateConfig,
            NamingCodeRegistryService codeRegistry,
            SystemRegistryService systemRegistry,
            AuthoringNamingService naming,
            IfcHeaderService header,
            SpaceZoneService spaceZone,
            ValidationService validation,
            DiagnosticsCollectorService diagnostics)
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

            _codeRegistry.SetEntries(_templateConfig.LoadEmbeddedNamingCodes());
            _systemRegistry.SetEntries(_templateConfig.LoadEmbeddedSystems());
        }

        public SetupCheckResult CheckSetup(Document doc, IList<ElementId> categoryIds)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Checking setup state.");
            var result = new SetupCheckResult { Status = "Ready" };
            var instanceNames = _cobieSync.GetStringParameters(doc, false).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var typeNames = _cobieSync.GetStringParameters(doc, true).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var param in RequiredParameters)
            {
                bool exists;
                bool writable = false;
                var notes = string.Empty;
                if (param.Scope == "Project")
                {
                    var projectParam = doc.ProjectInformation.LookupParameter(param.Name);
                    exists = projectParam != null;
                    writable = projectParam != null && !projectParam.IsReadOnly;
                    if (!exists) notes = "Missing project parameter";
                }
                else if (param.IsType)
                {
                    exists = typeNames.Contains(param.Name) || (param.Name == "IFCName [Type]" && typeNames.Contains("IFCName[Type]"));
                    writable = exists;
                    if (!exists) notes = "Missing type binding";
                }
                else
                {
                    exists = instanceNames.Contains(param.Name);
                    writable = exists;
                    if (!exists) notes = "Missing instance binding";
                }

                if (!exists) result.Status = "Warning";

                result.Parameters.Add(new RequiredParameterStatus
                {
                    ParameterName = param.Name,
                    Scope = param.Scope,
                    Exists = exists,
                    Writable = writable,
                    Notes = notes
                });
            }

            result.NamingMapLoaded = _codeRegistry.GetEntries().Any();
            result.SystemListLoaded = _systemRegistry.GetEntries().Any();
            result.Notes = $"Required parameters: {result.Parameters.Count(x => x.Exists)}/{result.Parameters.Count}. Naming codes: {_codeRegistry.GetEntries().Count}. Systems: {_systemRegistry.GetEntries().Count}.";
            return result;
        }

        public SetupCheckResult CreateMissingParameters(Document doc, IList<ElementId> categoryIds)
        {
            _diagnostics.AddInfo("Authoring.Setup", "Creating missing parameters via existing assign flow.");
            _cobieSync.AssignParameters(doc, categoryIds);
            return CheckSetup(doc, categoryIds);
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
            if (entries.Any()) _systemRegistry.SetEntries(entries);
            return _systemRegistry.GetEntries();
        }

        public NamingPreviewResult GenerateNamingPreview(Document doc, NamingGenerationRequest request)
        {
            _diagnostics.AddInfo("Authoring.Naming", "Generating naming preview.", new { request.ScopeMode, request.InstanceNumberingMode });
            return _naming.GeneratePreview(doc, request);
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
            _diagnostics.AddInfo("Authoring.SpaceZone", "Applying ZoneName values.");
            return _spaceZone.ApplyZone(doc, rows);
        }

        public SyncResult SyncCobie(Document doc)
        {
            _diagnostics.AddInfo("Authoring.COBie", "Running COBie sync with fixed IFC mappings.");
            return _cobieSync.ApplySync(doc, new MappingSettings
            {
                InstanceSource = "IFCName",
                InstanceTarget = "COBie.Component.Name",
                TypeSource = "IFCName [Type]",
                TypeTarget = "COBie.Type.Name",
                Scope = SyncScope.EntireModel,
                OverwriteMode = OverwriteMode.BlankOnly,
                CategoryIds = _cobieSync.GetModelCategories(doc).Select(c => c.Id.Value).ToList()
            });
        }

        public ValidationSummary BuildValidationSummary(Document doc, SetupCheckResult setup, NamingPreviewResult naming, HeaderValidationResult header, SpaceZonePreviewResult space)
        {
            return _validation.BuildSummary(doc, setup, naming, header, space);
        }

        public IList<Category> GetCategories(Document doc, IList<ElementId> selected = null) => _cobieSync.GetModelCategories(doc, selected);
        public IList<SystemRegistryEntry> GetSystems() => _systemRegistry.GetEntries();
    }
}
