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
    }

    public class RevitRequest
    {
        public RevitRequestId Id { get; set; }
        public MappingSettings Settings { get; set; }
        public IList<ElementId> CategoryIds { get; set; }
        public string ParameterName { get; set; }
        public Action<RevitResponse> Callback { get; set; }
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
        public string Error { get; set; }
    }
}
