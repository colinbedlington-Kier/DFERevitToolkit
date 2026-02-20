using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.Services;

namespace DfEIfcNamer.ExternalEvents
{
    public class RevitExecutionHandler : IExternalEventHandler
    {
        private readonly ParameterService _parameterService;
        private readonly NamingService _namingService;
        private readonly ResourceJsonService _resourceService;
        private readonly CounterStateService _counterService;
        private readonly AuditService _auditService;
        private RevitRequest _request;

        public RevitExecutionHandler(ParameterService parameterService, NamingService namingService, ResourceJsonService resourceService, CounterStateService counterService, AuditService auditService)
        {
            _parameterService = parameterService;
            _namingService = namingService;
            _resourceService = resourceService;
            _counterService = counterService;
            _auditService = auditService;
        }

        public void SetRequest(RevitRequest request) => _request = request;

        public void Execute(UIApplication app)
        {
            if (_request == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            switch (_request.Id)
            {
                case RevitRequestId.Bootstrap:
                    _parameterService.BootstrapSharedParameters(doc);
                    break;
                case RevitRequestId.ApplyTypeNames:
                    _namingService.ApplyTypeNaming(doc, _request.TypeRows, _resourceService.LoadClassificationSlots());
                    break;
                case RevitRequestId.ApplyInstanceNames:
                    _namingService.ApplyInstanceNaming(doc, _request.Scope, _request.NumberingMode, _resourceService.LoadEntityLibrary());
                    break;
                case RevitRequestId.ExportIfc:
                    _auditService.ExportIfcWithDfEPreset(doc);
                    break;
                case RevitRequestId.ExportAudit:
                    _auditService.ExportAuditCsv(doc);
                    break;
                case RevitRequestId.ResetCounters:
                    _counterService.ResetCounters(doc);
                    break;
                case RevitRequestId.SaveProjectConfig:
                    _resourceService.SaveProjectConfig(_request.JsonPayload);
                    break;
            }
        }

        public string GetName() => "DfE IFC Namer Handler";
    }
}
