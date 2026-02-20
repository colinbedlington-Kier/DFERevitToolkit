using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class AuditService
    {
        public void ExportAuditCsv(Document doc)
        {
            var rows = new List<string> { "ElementId,Category,TypeOrInstance,IfcName,IfcDescription,IfcExportAs,IfcExportType" };

            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements())
            {
                var type = doc.GetElement(e.GetTypeId());
                rows.Add(string.Join(",",
                    e.Id.IntegerValue,
                    Escape(e.Category?.Name),
                    "Instance",
                    Escape(e.LookupParameter("IfcName")?.AsString()),
                    Escape(e.LookupParameter("IfcDescription")?.AsString()),
                    Escape(type?.LookupParameter("IFC Export As")?.AsString()),
                    Escape(type?.LookupParameter("IFC Export Type")?.AsString())));
            }

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DfEIfcNamer", $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, rows, Encoding.UTF8);
        }

        public void ExportIfcWithDfEPreset(Document doc)
        {
            var options = new IFCExportOptions();
            options.FileVersion = IFCVersion.IFC2x3;
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DfEIfcNamer", "ifc");
            Directory.CreateDirectory(folder);
            doc.Export(folder, $"{Path.GetFileNameWithoutExtension(doc.PathName)}_DfE", options);
        }

        private static string Escape(string value)
        {
            var v = value ?? string.Empty;
            return $"\"{v.Replace("\"", "\"\"")}\"";
        }
    }
}
