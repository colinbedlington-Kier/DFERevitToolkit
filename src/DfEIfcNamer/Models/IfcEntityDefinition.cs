using System.Collections.Generic;

namespace DfEIfcNamer.Models
{
    public class IfcEntityDefinition
    {
        public string DisplayName { get; set; }
        public string IFCClassToken { get; set; }
        public string ExportAs { get; set; }
        public string ExportType { get; set; }
        public List<string> PredefinedTypes { get; set; } = new List<string>();
        public string NameFormat { get; set; }
    }
}
