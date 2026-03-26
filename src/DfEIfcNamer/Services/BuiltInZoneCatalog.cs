using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInZoneCatalog
    {
        private const string EmbeddedResource = "DfEIfcNamer.Resources.DfeZoneCatalog.csv";

        public static IList<ZoneCatalogEntry> Default()
        {
            return ReadEmbeddedCsv().Skip(1)
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

        private static IEnumerable<string> ReadEmbeddedCsv()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResource))
            using (var reader = new StreamReader(stream ?? throw new FileNotFoundException(EmbeddedResource)))
            {
                while (!reader.EndOfStream) yield return reader.ReadLine();
            }
        }

        private static string[] ParseCsvLine(string line) => (line ?? string.Empty).Split(',').Select(x => x.Trim(' ', '"')).ToArray();
    }
}
