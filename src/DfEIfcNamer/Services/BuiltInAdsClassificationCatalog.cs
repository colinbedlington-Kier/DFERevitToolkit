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

        private static string[] ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = string.Empty;
            var inQuotes = false;
            foreach (var ch in line ?? string.Empty)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    values.Add(current.Trim());
                    current = string.Empty;
                    continue;
                }

                current += ch;
            }

            values.Add(current.Trim());
            return values.ToArray();
        }
    }
}
