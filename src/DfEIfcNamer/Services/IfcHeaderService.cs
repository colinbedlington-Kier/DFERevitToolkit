using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class IfcHeaderService
    {
        private readonly ProjectParameterWriter _projectWriter = new ProjectParameterWriter();
        private static readonly string[] Required =
        {
            "IfcProjectName", "IfcProjectDescription", "IfcSiteName", "IfcSiteDescription",
            "IfcBuildingName", "IfcBuildingDescription", "UPRN", "MaximumBlockHeight"
        };

        public HeaderDataModel Read(Document doc)
        {
            var project = doc.ProjectInformation;
            return new HeaderDataModel
            {
                IfcProjectName = Get(project, ParameterWriteAliases.IfcProjectName),
                IfcProjectDescription = Get(project, ParameterWriteAliases.IfcProjectDescription),
                IfcSiteName = Get(project, ParameterWriteAliases.IfcSiteName),
                IfcSiteDescription = Get(project, ParameterWriteAliases.IfcSiteDescription),
                IfcBuildingName = Get(project, ParameterWriteAliases.IfcBuildingName),
                IfcBuildingDescription = Get(project, ParameterWriteAliases.IfcBuildingDescription),
                UPRN = Get(project, "UPRN"),
                MaximumBlockHeight = Get(project, "MaximumBlockHeight")
            };
        }

        public HeaderValidationResult Validate(HeaderDataModel data)
        {
            var result = new HeaderValidationResult { IsValid = true };
            if (string.IsNullOrWhiteSpace(data?.IfcProjectName)) result.Messages.Add("IfcProjectName is required.");
            if (string.IsNullOrWhiteSpace(data?.IfcSiteName)) result.Messages.Add("IfcSiteName is recommended and currently blank.");
            if (string.IsNullOrWhiteSpace(data?.IfcBuildingName)) result.Messages.Add("IfcBuildingName is recommended and currently blank.");
            if (!string.IsNullOrWhiteSpace(data?.UPRN) && (data.UPRN.Length < 6 || data.UPRN.Length > 24 || data.UPRN.Any(ch => !char.IsLetterOrDigit(ch))))
            {
                result.Messages.Add("UPRN format is not valid (expected alphanumeric between 6 and 24 chars).");
            }

            if (!string.IsNullOrWhiteSpace(data?.MaximumBlockHeight) && !double.TryParse(data.MaximumBlockHeight, out _))
            {
                result.Messages.Add("MaximumBlockHeight must be numeric.");
            }

            result.IsValid = result.Messages.All(m => !m.Contains("required", StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public ApplyResult Write(Document doc, HeaderDataModel data)
        {
            var result = new ApplyResult();
            using (var tx = new Transaction(doc, "DfE IFC Header Data"))
            {
                tx.Start();
                foreach (var pair in ToPairs(data))
                {
                    var names = pair.Key.Split('|');
                    if (_projectWriter.Write(doc, pair.Value, names))
                    {
                        result.Updated++;
                    }
                    else
                    {
                        result.Skipped++;
                        result.Logs.Add("Missing/read-only project parameter: " + pair.Key);
                    }
                }

                tx.Commit();
            }

            return result;
        }

        public IList<RequiredParameterStatus> GetRequiredHeaderStatus(Document doc)
        {
            var project = doc.ProjectInformation;
            return Required.Select(name =>
            {
                var p = project.LookupParameter(name);
                return new RequiredParameterStatus
                {
                    ParameterName = name,
                    Scope = "Project",
                    Exists = p != null,
                    Writable = p != null && !p.IsReadOnly,
                    Notes = p == null ? "Missing" : ""
                };
            }).ToList();
        }

        private static IDictionary<string, string> ToPairs(HeaderDataModel d)
        {
            return new Dictionary<string, string>
            {
                ["IfcProjectName|Project Name"] = d?.IfcProjectName,
                ["IfcProjectDescription|Project Description"] = d?.IfcProjectDescription,
                ["IfcSiteName|Site Name"] = d?.IfcSiteName,
                ["IfcSiteDescription|Site Description"] = d?.IfcSiteDescription,
                ["IfcBuildingName|Building Name"] = d?.IfcBuildingName,
                ["IfcBuildingDescription|Building Description"] = d?.IfcBuildingDescription,
                ["UPRN"] = d?.UPRN,
                ["MaximumBlockHeight"] = d?.MaximumBlockHeight
            };
        }

        private static string Get(Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var p = element?.LookupParameter(name);
                if (p != null) return p.AsString() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
