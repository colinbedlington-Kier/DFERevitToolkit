using System;
using System.Collections.Generic;
using System.Linq;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInAdsClassificationCatalog
    {
        private const string FileName = "DfeAdsCatalog.csv";

        public static IList<AdsClassificationEntry> Default(string explicitPath = null)
        {
            var loader = new ResourceFileLoader();
            var lines = loader.LoadCsvResourceOrFile(FileName, explicitPath);

            return lines.Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(ParseCsvLine)
                .Where(parts => parts.Length >= 2)
                .Select(parts => new AdsClassificationEntry { Code = parts[0], Description = parts[1] })
                .ToList();
        }

        private static string[] ParseCsvLine(string line) => (line ?? string.Empty).Split(',').Select(x => x.Trim()).ToArray();
    }
}
