using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private const string EmbeddedManifestResource = "DfEIfcNamer.Resources.DfeParameterBindingManifest.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static IList<ParameterBindingManifestEntry> _cache;

        public static IList<ParameterBindingManifestEntry> All()
        {
            if (_cache != null) return _cache.ToList();
            var json = ReadWithFallback();
            _cache = JsonSerializer.Deserialize<List<ParameterBindingManifestEntry>>(json, JsonOptions) ?? new List<ParameterBindingManifestEntry>();
            return _cache.ToList();
        }

        public static ParameterBindingManifestEntry FindByName(string parameterName)
        {
            return All().FirstOrDefault(x =>
                string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase) ||
                x.Aliases.Any(a => string.Equals(a, parameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private static string ReadWithFallback()
        {
            var addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            var resourcesPath = Path.Combine(addinFolder, "Resources", ManifestFileName);
            var directPath = Path.Combine(addinFolder, ManifestFileName);
            if (File.Exists(resourcesPath)) return File.ReadAllText(resourcesPath);
            if (File.Exists(directPath)) return File.ReadAllText(directPath);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedManifestResource))
            {
                if (stream == null) throw new FileNotFoundException("Missing embedded manifest resource: " + EmbeddedManifestResource);
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
