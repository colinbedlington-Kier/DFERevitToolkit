using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.Models;
using DfEIfcNamer.Services;

namespace DfEIfcNamer.ExternalEvents
{
    public class RevitExecutionHandler : IExternalEventHandler
    {
        private readonly CobieSyncService _cobieSyncService;
        private readonly ProjectSettingsStore _settingsStore;
        private readonly IfcAuthoringService _authoringService;
        private RevitRequest _request;

        public RevitExecutionHandler(CobieSyncService cobieSyncService, ProjectSettingsStore settingsStore, IfcAuthoringService authoringService)
        {
            _cobieSyncService = cobieSyncService;
            _settingsStore = settingsStore;
            _authoringService = authoringService;
        }

        public void SetRequest(RevitRequest request) => _request = request;

        public void Execute(UIApplication app)
        {
            var response = new RevitResponse();
            try
            {
                if (_request == null) return;

                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    response.Error = "No active document.";
                    return;
                }

                switch (_request.Id)
                {
                    case RevitRequestId.CheckSetup:
                        response.SetupStatus = _cobieSyncService.CheckSetup(doc, _request.CategoryIds);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.AssignParameters:
                        response.SetupStatus = _cobieSyncService.AssignParameters(doc, _request.CategoryIds);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.CheckAuthoringSetup:
                        response.AuthoringSetup = _authoringService.CheckSetup(doc, _request.CategoryIds);
                        response.Systems = _authoringService.GetSystems();
                        response.Zones = _authoringService.GetZones();
                        response.AdsClassifications = _authoringService.GetAdsClassifications();
                        break;
                    case RevitRequestId.CreateAuthoringParameters:
                        response.AuthoringSetup = _authoringService.CreateMissingParameters(doc, _request.CategoryIds);
                        response.Systems = _authoringService.GetSystems();
                        response.Zones = _authoringService.GetZones();
                        response.AdsClassifications = _authoringService.GetAdsClassifications();
                        break;
                    case RevitRequestId.LoadNamingCodeMap:
                        response.NamingCodes = _authoringService.LoadNamingCodes(_request.ExternalPath);
                        break;
                    case RevitRequestId.LoadSystemList:
                        response.Systems = _authoringService.LoadSystems(_request.ExternalPath);
                        break;
                    case RevitRequestId.GenerateNamingPreview:
                        response.NamingPreview = _authoringService.GenerateNamingPreview(doc, _request.NamingRequest ?? new NamingGenerationRequest());
                        break;
                    case RevitRequestId.ApplyNamingInstance:
                        response.ApplyResult = _authoringService.ApplyNaming(doc, _request.NamingRows, true, false, false);
                        break;
                    case RevitRequestId.ApplyNamingType:
                        response.ApplyResult = _authoringService.ApplyNaming(doc, _request.NamingRows, false, true, false);
                        break;
                    case RevitRequestId.ApplySystemData:
                        response.ApplyResult = _authoringService.ApplyNaming(doc, _request.NamingRows, false, false, true);
                        break;
                    case RevitRequestId.ApplyNamingAll:
                        response.ApplyResult = _authoringService.ApplyNaming(doc, _request.NamingRows, true, true, true);
                        break;
                    case RevitRequestId.ReadHeaderData:
                        response.HeaderData = _authoringService.ReadHeader(doc);
                        break;
                    case RevitRequestId.ValidateHeaderData:
                        response.HeaderValidation = _authoringService.ValidateHeader(_request.HeaderData);
                        break;
                    case RevitRequestId.WriteHeaderData:
                        response.ApplyResult = _authoringService.WriteHeader(doc, _request.HeaderData);
                        response.HeaderValidation = _authoringService.ValidateHeader(_request.HeaderData);
                        break;
                    case RevitRequestId.ResolveSpaceZone:
                        response.SpaceZonePreview = _authoringService.BuildSpaceZonePreview(doc, _request.SpaceZoneRequest ?? new SpaceZoneRequest());
                        response.Zones = _authoringService.GetZones();
                        response.AdsClassifications = _authoringService.GetAdsClassifications();
                        break;
                    case RevitRequestId.ApplySpaceReference:
                        response.ApplyResult = _authoringService.ApplySpaceReference(doc, _request.SpaceZoneRows);
                        break;
                    case RevitRequestId.ApplyZoneName:
                        response.ApplyResult = _authoringService.ApplyZone(doc, _request.SpaceZoneRows);
                        break;
                    case RevitRequestId.GenerateClassificationSyncPreview:
                        response.ClassificationSyncResult = _authoringService.BuildClassificationSyncPreview(doc, _request.NamingRequest?.CategoryIds);
                        break;
                    case RevitRequestId.ApplyClassificationSync:
                        response.ApplyResult = _authoringService.ApplyClassificationSync(doc, _request.ClassificationSyncRows);
                        break;
                    case RevitRequestId.GetExistingSystemNames:
                        response.ExistingSystemNames = _authoringService.GetExistingSystemNames(doc);
                        break;
                    case RevitRequestId.RunComplianceValidation:
                        response.ComplianceSummary = _authoringService.BuildComplianceReport(doc, _request.NamingRequest?.CategoryIds);
                        break;
                    case RevitRequestId.OpenComplianceReview3d:
                        response.ApplyResult = _authoringService.OpenComplianceReview3d(app, _request.ElementIds);
                        break;
                    case RevitRequestId.SyncCobieFromIfc:
                        response.SyncResult = _authoringService.SyncCobie(doc);
                        break;
                    case RevitRequestId.RunAuthoringValidation:
                        response.ValidationSummary = _authoringService.BuildValidationSummary(doc, _request.SetupSnapshot, _request.NamingSnapshot, _request.HeaderSnapshot, _request.SpaceZoneSnapshot);
                        break;
                    case RevitRequestId.GetAvailableParameters:
                        response.InstanceParameters = _cobieSyncService.GetStringParameters(doc, false);
                        response.TypeParameters = _cobieSyncService.GetStringParameters(doc, true);
                        break;
                    case RevitRequestId.GetCategories:
                        response.Categories = _cobieSyncService.GetModelCategories(doc);
                        break;
                    case RevitRequestId.RunFullDiagnostics:
                        _cobieSyncService.RunFullDiagnostics(doc, _request.CategoryIds);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.CheckSharedParameterFile:
                        _cobieSyncService.RunSharedParameterFileInspection(doc);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.CheckExpectedDefinitions:
                        _cobieSyncService.RunExpectedDefinitionDiagnostics(doc);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.CheckCategoryBindings:
                        _cobieSyncService.RunCategoryBindingDiagnostics(doc, _request.CategoryIds);
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.TestSingleParameterBind:
                        using (var tx = new Transaction(doc, "DfE IFC Namer - Single Parameter Diagnostic"))
                        {
                            tx.Start();
                            _cobieSyncService.RunSingleParameterBindDiagnostic(doc, _request.CategoryIds, _request.ParameterName);
                            tx.Commit();
                        }
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.GetDiagnosticsState:
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.ClearDiagnostics:
                        _cobieSyncService.ClearDiagnostics();
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                    case RevitRequestId.LoadMapping:
                        response.Settings = _settingsStore.Load(doc) ?? new MappingSettings();
                        break;
                    case RevitRequestId.SaveMapping:
                        using (var tx = new Transaction(doc, "DfE IFC Namer - Save Mapping"))
                        {
                            tx.Start();
                            _settingsStore.Save(doc, _request.Settings ?? new MappingSettings());
                            tx.Commit();
                        }
                        response.Settings = _request.Settings;
                        break;
                    case RevitRequestId.ApplySync:
                        response.SyncResult = _cobieSyncService.ApplySync(doc, _request.Settings ?? new MappingSettings());
                        using (var tx = new Transaction(doc, "DfE IFC Namer - Save Mapping"))
                        {
                            tx.Start();
                            _settingsStore.Save(doc, _request.Settings ?? new MappingSettings());
                            tx.Commit();
                        }
                        response.DiagnosticsState = _cobieSyncService.GetDiagnosticsState();
                        break;
                }
            }
            catch (Exception ex)
            {
                response.Error = ex.ToString();
            }
            finally
            {
                _request?.Callback?.Invoke(response);
            }
        }

        public string GetName() => "DfE IFC Namer Handler";
    }
}
