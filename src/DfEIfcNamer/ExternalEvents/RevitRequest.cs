using System.Collections.Generic;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.ExternalEvents
{
    public enum RevitRequestId
    {
        Bootstrap,
        ApplyTypeNames,
        ApplyInstanceNames,
        ExportIfc,
        ExportAudit,
        ResetCounters,
        SaveProjectConfig
    }

    public class RevitRequest
    {
        public RevitRequestId Id { get; set; }
        public IList<TypeRowModel> TypeRows { get; set; }
        public string NumberingMode { get; set; }
        public string Scope { get; set; }
        public string JsonPayload { get; set; }
    }
}
