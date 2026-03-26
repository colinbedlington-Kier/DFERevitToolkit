using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class TemplateConfigService
    {
        private const string NamingFileName = "default_naming_codes.json";
        private const string SystemsFileName = "DfeSystemCatalog.csv";
        private readonly ResourceFileLoader _resourceLoader = new ResourceFileLoader();

        public IList<NamingCodeMapEntry> LoadEmbeddedNamingCodes()
        {
            return _resourceLoader.LoadJsonResourceOrFile<List<NamingCodeMapEntry>>(NamingFileName, null, JsonOptions()) ?? new List<NamingCodeMapEntry>();
        }

        public IList<SystemRegistryEntry> LoadEmbeddedSystems()
        {
            var csvLines = _resourceLoader.LoadCsvResourceOrFile(SystemsFileName);
            return ParseSystemsCsv(csvLines);
        }

        public IList<NamingCodeMapEntry> LoadNamingCodesFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<NamingCodeMapEntry>();
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseNamingCsv(File.ReadAllLines(path));
            }

            return JsonSerializer.Deserialize<List<NamingCodeMapEntry>>(File.ReadAllText(path), JsonOptions()) ?? new List<NamingCodeMapEntry>();
        }

        public IList<SystemRegistryEntry> LoadSystemsFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<SystemRegistryEntry>();
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSystemsCsv(File.ReadAllLines(path));
            }

            return JsonSerializer.Deserialize<List<SystemRegistryEntry>>(File.ReadAllText(path), JsonOptions()) ?? new List<SystemRegistryEntry>();
        }

        public void SaveHeaderTemplate(string path, HeaderDataModel model)
        {
            var json = JsonSerializer.Serialize(model ?? new HeaderDataModel(), JsonOptions());
            File.WriteAllText(path, json);
        }

        public HeaderDataModel LoadHeaderTemplate(string path)
        {
            if (!File.Exists(path)) return new HeaderDataModel();
            return JsonSerializer.Deserialize<HeaderDataModel>(File.ReadAllText(path), JsonOptions()) ?? new HeaderDataModel();
        }

        private static JsonSerializerOptions JsonOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static IList<NamingCodeMapEntry> ParseNamingCsv(IEnumerable<string> lines)
        {
            return lines.Skip(1)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 3)
                .Select(parts => new NamingCodeMapEntry
                {
                    IfcClass = parts[0].Trim(),
                    PredefinedType = parts[1].Trim(),
                    Code = parts[2].Trim()
                })
                .ToList();
        }

        private static IList<SystemRegistryEntry> ParseSystemsCsv(IEnumerable<string> lines)
        {
            return lines.Skip(1)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 2)
                .Select(parts => new SystemRegistryEntry
                {
                    SystemName = parts[0].Trim(),
                    SystemDescription = parts[1].Trim(),
                    Discipline = parts.Length >= 3 ? parts[2].Trim() : string.Empty,
                    AllowedCategories = parts.Length >= 4 ? parts[3].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList() : new List<string>(),
                    AllowedIfcClasses = parts.Length >= 5 ? parts[4].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList() : new List<string>()
                })
                .ToList();
        }
    }
}
