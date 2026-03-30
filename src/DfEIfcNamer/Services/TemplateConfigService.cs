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
        private const string SystemsFileName = "dfe_system_catalog.json";
        private readonly ResourceFileLoader _resourceLoader = new ResourceFileLoader();
        public string LastNamingCodesSource { get; private set; } = "embedded";
        public string LastSystemsSource { get; private set; } = "embedded";

        public IList<NamingCodeMapEntry> LoadEmbeddedNamingCodes()
        {
            var externalPath = _resourceLoader.ResolveExternalResourcePath(NamingFileName);
            LastNamingCodesSource = File.Exists(externalPath) ? $"external:{externalPath}" : $"embedded:{_resourceLoader.ResolveEmbeddedResourceName(NamingFileName)}";
            return _resourceLoader.LoadJsonResourceOrFile<List<NamingCodeMapEntry>>(NamingFileName, null, JsonOptions()) ?? new List<NamingCodeMapEntry>();
        }

        public IList<SystemRegistryEntry> LoadEmbeddedSystems()
        {
            var externalPath = _resourceLoader.ResolveExternalResourcePath(SystemsFileName);
            LastSystemsSource = File.Exists(externalPath) ? $"external:{externalPath}" : $"embedded:{_resourceLoader.ResolveEmbeddedResourceName(SystemsFileName)}";
            var payload = _resourceLoader.LoadTextResourceOrFile(SystemsFileName);
            return ParseSystemsJson(payload);
        }

        public IList<NamingCodeMapEntry> LoadNamingCodesFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<NamingCodeMapEntry>();
            LastNamingCodesSource = "override:" + path;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseNamingCsv(File.ReadAllLines(path));
            }

            return JsonSerializer.Deserialize<List<NamingCodeMapEntry>>(File.ReadAllText(path), JsonOptions()) ?? new List<NamingCodeMapEntry>();
        }

        public IList<SystemRegistryEntry> LoadSystemsFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<SystemRegistryEntry>();
            LastSystemsSource = "override:" + path;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSystemsCsv(File.ReadAllLines(path));
            }
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ParseSystemsJson(File.ReadAllText(path))
                : ParseSystemsCsv(File.ReadAllLines(path));
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
            var allLines = lines?.ToList() ?? new List<string>();
            if (allLines.Count < 2) return new List<SystemRegistryEntry>();
            var headers = allLines[0].Split(',').Select(h => h.Trim()).ToList();
            int HeaderIndex(params string[] names) =>
                headers.FindIndex(h => names.Any(n => string.Equals(h, n, StringComparison.OrdinalIgnoreCase)));

            var nameIx = HeaderIndex("SystemName", "Name", "System");
            var descIx = HeaderIndex("SystemDescription", "Description");
            var disciplineIx = HeaderIndex("Discipline");
            var categoriesIx = HeaderIndex("AllowedCategories", "Categories");
            var ifcIx = HeaderIndex("AllowedIfcClasses", "IfcClasses");

            return allLines.Skip(1)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 1)
                .Select(parts => new SystemRegistryEntry
                {
                    SystemName = Read(parts, nameIx),
                    SystemDescription = Read(parts, descIx),
                    Discipline = Read(parts, disciplineIx),
                    AllowedCategories = SplitList(Read(parts, categoriesIx)),
                    AllowedIfcClasses = SplitList(Read(parts, ifcIx))
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SystemName))
                .ToList();
        }

        private static IList<SystemRegistryEntry> ParseSystemsJson(string json)
        {
            var entries = new List<SystemRegistryEntry>();
            using (var doc = JsonDocument.Parse(json))
            {
                var sourceArray = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray()
                    : (doc.RootElement.TryGetProperty("systems", out var systems) && systems.ValueKind == JsonValueKind.Array
                        ? systems.EnumerateArray()
                        : Enumerable.Empty<JsonElement>());

                foreach (var row in sourceArray)
                {
                    var name = Read(row, "SystemName", "Name", "System", "systemNameTemplate");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    entries.Add(new SystemRegistryEntry
                    {
                        SystemName = name,
                        SystemDescription = Read(row, "SystemDescription", "Description", "baseDescription"),
                        Discipline = Read(row, "Discipline"),
                        AllowedCategories = ReadArray(row, "AllowedCategories", "Categories"),
                        AllowedIfcClasses = ReadArray(row, "AllowedIfcClasses", "IfcClasses"),
                        AllowedCategoryPrefixes = ReadArray(row, "allowedCategoryPrefixes", "AllowedCategoryPrefixes")
                    });
                }
            }

            return entries;
        }

        private static string Read(string[] parts, int index) => index >= 0 && index < parts.Length ? parts[index].Trim() : string.Empty;
        private static List<string> SplitList(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

        private static string Read(JsonElement row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetProperty(name, out var value))
                {
                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                }
            }

            return string.Empty;
        }

        private static List<string> ReadArray(JsonElement row, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetProperty(name, out var value)) continue;
                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return SplitList(value.GetString());
                }
            }

            return new List<string>();
        }
    }
}
