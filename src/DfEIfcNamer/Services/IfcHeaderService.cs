using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class IfcHeaderService
    {
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
                IfcProjectName = Get(project, "IfcProjectName"),
                IfcProjectDescription = Get(project, "IfcProjectDescription"),
                IfcSiteName = Get(project, "IfcSiteName"),
                IfcSiteDescription = Get(project, "IfcSiteDescription"),
                IfcBuildingName = Get(project, "IfcBuildingName"),
                IfcBuildingDescription = Get(project, "IfcBuildingDescription"),
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
                var project = doc.ProjectInformation;
                foreach (var pair in ToPairs(data))
                {
                    var p = project.LookupParameter(pair.Key);
                    if (p == null)
                    {
                        result.Skipped++;
                        result.Logs.Add("Missing project parameter: " + pair.Key);
                        continue;
                    }

                    if (p.IsReadOnly)
                    {
                        result.Skipped++;
                        result.Logs.Add("Read-only project parameter: " + pair.Key);
                        continue;
                    }

                    p.Set(pair.Value ?? string.Empty);
                    result.Updated++;
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
                ["IfcProjectName"] = d?.IfcProjectName,
                ["IfcProjectDescription"] = d?.IfcProjectDescription,
                ["IfcSiteName"] = d?.IfcSiteName,
                ["IfcSiteDescription"] = d?.IfcSiteDescription,
                ["IfcBuildingName"] = d?.IfcBuildingName,
                ["IfcBuildingDescription"] = d?.IfcBuildingDescription,
                ["UPRN"] = d?.UPRN,
                ["MaximumBlockHeight"] = d?.MaximumBlockHeight
            };
        }

        private static string Get(Element element, string name) => element?.LookupParameter(name)?.AsString() ?? string.Empty;
    }
}
