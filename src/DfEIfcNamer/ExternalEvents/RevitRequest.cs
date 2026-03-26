using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.ExternalEvents
{
    public enum RevitRequestId
    {
        CheckSetup,
        AssignParameters,
        CheckAuthoringSetup,
        CreateAuthoringParameters,
        LoadNamingCodeMap,
        LoadSystemList,
        GenerateNamingPreview,
        ApplyNamingInstance,
        ApplyNamingType,
        ApplySystemData,
        ApplyNamingAll,
        ReadHeaderData,
        ValidateHeaderData,
        WriteHeaderData,
        ResolveSpaceZone,
        ApplySpaceReference,
        ApplyZoneName,
        RunAuthoringValidation,
        SyncCobieFromIfc,
        LoadMapping,
        SaveMapping,
        ApplySync,
        GetAvailableParameters,
        GetCategories,
        RunFullDiagnostics,
        CheckSharedParameterFile,
        CheckExpectedDefinitions,
        CheckCategoryBindings,
        TestSingleParameterBind,
        GetDiagnosticsState,
        ClearDiagnostics
        ,
        GenerateClassificationSyncPreview,
        ApplyClassificationSync
    }

    public class RevitRequest
    {
        public RevitRequestId Id { get; set; }
        public MappingSettings Settings { get; set; }
        public IList<ElementId> CategoryIds { get; set; }
        public string ParameterName { get; set; }
        public string ExternalPath { get; set; }
        public NamingGenerationRequest NamingRequest { get; set; }
        public IList<NamingPreviewRow> NamingRows { get; set; }
        public HeaderDataModel HeaderData { get; set; }
        public SpaceZoneRequest SpaceZoneRequest { get; set; }
        public IList<SpaceZonePreviewRow> SpaceZoneRows { get; set; }
        public SetupCheckResult SetupSnapshot { get; set; }
        public NamingPreviewResult NamingSnapshot { get; set; }
        public HeaderValidationResult HeaderSnapshot { get; set; }
        public SpaceZonePreviewResult SpaceZoneSnapshot { get; set; }
        public Action<RevitResponse> Callback { get; set; }
        public IList<ClassificationSyncPreviewRow> ClassificationSyncRows { get; set; }
    }

    public class RevitResponse
    {
        public SetupStatus SetupStatus { get; set; }
        public MappingSettings Settings { get; set; }
        public SyncResult SyncResult { get; set; }
        public IList<ProjectParameterOption> InstanceParameters { get; set; }
        public IList<ProjectParameterOption> TypeParameters { get; set; }
        public IList<Category> Categories { get; set; }
        public DiagnosticsState DiagnosticsState { get; set; }
        public SetupCheckResult AuthoringSetup { get; set; }
        public IList<NamingCodeMapEntry> NamingCodes { get; set; }
        public IList<SystemRegistryEntry> Systems { get; set; }
        public NamingPreviewResult NamingPreview { get; set; }
        public ApplyResult ApplyResult { get; set; }
        public HeaderDataModel HeaderData { get; set; }
        public HeaderValidationResult HeaderValidation { get; set; }
        public SpaceZonePreviewResult SpaceZonePreview { get; set; }
        public ValidationSummary ValidationSummary { get; set; }
        public IList<ZoneCatalogEntry> Zones { get; set; }
        public IList<AdsClassificationEntry> AdsClassifications { get; set; }
        public ClassificationSyncResult ClassificationSyncResult { get; set; }
        public string Error { get; set; }
    }
}
