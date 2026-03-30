using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class SystemCatalogService
    {
        private const string CatalogFileName = "dfe_system_catalog.json";
        private readonly ResourceFileLoader _loader = new ResourceFileLoader();
        private readonly object _sync = new object();
        private List<SystemRegistryEntry> _cachedEntries;
        private string _cachedError;

        public IList<SystemRegistryEntry> LoadEntries(string explicitPath = null)
        {
            lock (_sync)
            {
                if (_cachedEntries != null && string.IsNullOrWhiteSpace(explicitPath)) return _cachedEntries;
                try
                {
                    var json = _loader.LoadTextResourceOrFile(CatalogFileName, explicitPath);
                    var entries = ParseEntries(json);
                    _cachedEntries = entries;
                    _cachedError = null;
                    return entries;
                }
                catch (Exception ex)
                {
                    _cachedError = ex.Message;
                    return new List<SystemRegistryEntry>();
                }
            }
        }

        public string GetLastError() => _cachedError;

        public SystemRegistryEntry ResolveByClassification(string ssNumber)
        {
            if (string.IsNullOrWhiteSpace(ssNumber)) return null;

            var entries = LoadEntries();
            return entries
                .SelectMany(entry => (entry.AllowedCategoryPrefixes ?? new List<string>())
                    .Where(prefix => !string.IsNullOrWhiteSpace(prefix) && ssNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(prefix => new { Entry = entry, Prefix = prefix }))
                .OrderByDescending(x => x.Prefix.Length)
                .Select(x => new SystemRegistryEntry
                {
                    SystemName = x.Entry.SystemName,
                    SystemDescription = x.Entry.SystemDescription,
                    AllowedCategoryPrefixes = x.Entry.AllowedCategoryPrefixes?.ToList() ?? new List<string>(),
                    AllowedCategories = x.Entry.AllowedCategories?.ToList() ?? new List<string>(),
                    AllowedIfcClasses = x.Entry.AllowedIfcClasses?.ToList() ?? new List<string>(),
                    Discipline = x.Entry.Discipline,
                    MatchedPrefix = x.Prefix
                })
                .FirstOrDefault();
        }

        private static List<SystemRegistryEntry> ParseEntries(string json)
        {
            var entries = new List<SystemRegistryEntry>();
            using (var doc = JsonDocument.Parse(json ?? "{}"))
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    entries.AddRange(root.EnumerateArray().Select(ParseEntry).Where(x => x != null));
                }
                else if (root.TryGetProperty("systems", out var systems) && systems.ValueKind == JsonValueKind.Array)
                {
                    entries.AddRange(systems.EnumerateArray().Select(ParseEntry).Where(x => x != null));
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    var single = ParseEntry(root);
                    if (single != null) entries.Add(single);
                }
            }

            return entries.Where(e => !string.IsNullOrWhiteSpace(e.SystemName)).ToList();
        }

        private static SystemRegistryEntry ParseEntry(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            var name = ReadString(element, "systemNameTemplate", "systemName", "name");
            var description = ReadString(element, "baseDescription", "systemDescription", "description");
            var prefixes = ReadArray(element, "allowedCategoryPrefixes", "prefixes");

            return new SystemRegistryEntry
            {
                SystemName = name,
                SystemDescription = description,
                AllowedCategoryPrefixes = prefixes,
                Discipline = "Catalog"
            };
        }

        private static string ReadString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return string.Empty;
        }

        private static List<string> ReadArray(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }
            }

            return new List<string>();
        }
    }
}
