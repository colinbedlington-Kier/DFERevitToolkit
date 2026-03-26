using System;
using System.Collections.Generic;
using System.Linq;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInZoneCatalog
    {
        private const string FileName = "DfeZoneCatalog.csv";

        public static IList<ZoneCatalogEntry> Default(string explicitPath = null)
        {
            var loader = new ResourceFileLoader();
            var lines = loader.LoadCsvResourceOrFile(FileName, explicitPath);

            return lines.Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(ParseCsvLine)
                .Where(parts => parts.Length >= 5)
                .Select(parts => new ZoneCatalogEntry
                {
                    Name = parts[0],
                    Description = parts[1],
                    Category = parts[2],
                    Hex = parts[3],
                    Rgb = parts[4]
                })
                .ToList();
        }

        private static string[] ParseCsvLine(string line) => (line ?? string.Empty).Split(',').Select(x => x.Trim(' ', '"')).ToArray();
    }
}
