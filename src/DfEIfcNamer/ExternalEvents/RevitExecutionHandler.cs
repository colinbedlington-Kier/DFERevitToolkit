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
        private RevitRequest _request;

        public RevitExecutionHandler(CobieSyncService cobieSyncService, ProjectSettingsStore settingsStore)
        {
            _cobieSyncService = cobieSyncService;
            _settingsStore = settingsStore;
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
