using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class TemplateConfigService
    {
        private const string NamingResource = "DfEIfcNamer.Resources.default_naming_codes.json";
        private const string SystemsResource = "DfEIfcNamer.Resources.default_systems.json";

        public IList<NamingCodeMapEntry> LoadEmbeddedNamingCodes()
        {
            var json = ReadEmbedded(NamingResource);
            return JsonSerializer.Deserialize<List<NamingCodeMapEntry>>(json, JsonOptions()) ?? new List<NamingCodeMapEntry>();
        }

        public IList<SystemRegistryEntry> LoadEmbeddedSystems()
        {
            var json = ReadEmbedded(SystemsResource);
            return JsonSerializer.Deserialize<List<SystemRegistryEntry>>(json, JsonOptions()) ?? new List<SystemRegistryEntry>();
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

        private static string ReadEmbedded(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Missing embedded resource: " + resource);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
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
                    Discipline = parts.Length >= 3 ? parts[2].Trim() : string.Empty
                })
                .ToList();
        }
    }
}
