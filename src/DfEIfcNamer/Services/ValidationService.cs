using System.Linq;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ValidationService
    {
        public ValidationSummary BuildSummary(Document doc, SetupCheckResult setup, NamingPreviewResult naming, HeaderValidationResult header, SpaceZonePreviewResult space)
        {
            var summary = new ValidationSummary
            {
                SetupReadiness = setup?.Status ?? "Unknown",
                NamingCompleteness = naming == null ? "Unknown" : $"Eligible: {naming.EligibleCount}, Errors: {naming.ErrorCount}",
                HeaderCompleteness = header?.IsValid == true ? "Valid" : "Warnings/Errors",
                SpaceZoneCompleteness = space == null ? "Unknown" : $"Missing room assignments: {space.MissingRoomCount}"
            };

            if (naming?.Warnings?.Any() == true)
            {
                foreach (var warning in naming.Warnings.Take(20)) summary.Messages.Add(warning);
            }

            if (header?.Messages != null)
            {
                foreach (var m in header.Messages.Take(20)) summary.Messages.Add(m);
            }

            return summary;
        }
    }
}
