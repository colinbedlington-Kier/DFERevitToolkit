using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DfEIfcNamer.Services
{
    public enum ParameterScopeKind
    {
        Instance,
        Type,
        Project
    }

    public class ParameterBindingManifestEntry
    {
        public string Name { get; set; }
        public ParameterScopeKind Scope { get; set; }
        public bool RequiredForSetupCheck { get; set; }
        public string Usage { get; set; }
        public string[] Aliases { get; set; } = Array.Empty<string>();
        public string[] Categories { get; set; } = Array.Empty<string>();
    }

    public static class ParameterBindingManifest
    {
        private const string ManifestFileName = "DfeParameterBindingManifest.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly ResourceFileLoader ResourceLoader = new ResourceFileLoader();
        private static IList<ParameterBindingManifestEntry> _cache;

        public static IList<ParameterBindingManifestEntry> All()
        {
            if (_cache != null) return _cache.ToList();
            var json = ResourceLoader.LoadTextResourceOrFile(ManifestFileName);
            _cache = JsonSerializer.Deserialize<List<ParameterBindingManifestEntry>>(json, JsonOptions) ?? new List<ParameterBindingManifestEntry>();
            return _cache.ToList();
        }

        public static ParameterBindingManifestEntry FindByName(string parameterName)
        {
            return All().FirstOrDefault(x =>
                string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase) ||
                x.Aliases.Any(a => string.Equals(a, parameterName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
