using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInAdsClassificationCatalog
    {
        private const string EmbeddedResource = "DfEIfcNamer.Resources.DfeAdsCatalog.csv";

        public static IList<AdsClassificationEntry> Default()
        {
            return ReadEmbeddedCsv().Skip(1)
                .Select(ParseCsvLine)
                .Where(parts => parts.Length >= 2)
                .Select(parts => new AdsClassificationEntry { Code = parts[0], Description = parts[1] })
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

        private static string[] ParseCsvLine(string line) => (line ?? string.Empty).Split(',').Select(x => x.Trim()).ToArray();
    }
}
