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

    public class ParameterManifestLoadDiagnostics
    {
        public int TotalRows { get; set; }
        public int ParsedRows { get; set; }
        public int FailedRows => Errors?.Count ?? 0;
        public IList<string> Errors { get; set; } = new List<string>();
    }

    internal class ParameterManifestRowDto
    {
        public string name { get; set; }
        public string scope { get; set; }
        public bool requiredForSetupCheck { get; set; }
        public string usage { get; set; }
        public string[] aliases { get; set; }
        public string[] categories { get; set; }
    }

    public static class ParameterBindingManifest
    {
        private const string ManifestFileName = "DfeParameterBindingManifest.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly ResourceFileLoader ResourceLoader = new ResourceFileLoader();
        private static IList<ParameterBindingManifestEntry> _cache;
        private static ParameterManifestLoadDiagnostics _diagnostics = new ParameterManifestLoadDiagnostics();

        public static ParameterManifestLoadDiagnostics LastDiagnostics => new ParameterManifestLoadDiagnostics
        {
            TotalRows = _diagnostics.TotalRows,
            ParsedRows = _diagnostics.ParsedRows,
            Errors = _diagnostics.Errors.ToList()
        };

        public static IList<ParameterBindingManifestEntry> All()
        {
            if (_cache != null) return _cache.ToList();
            var json = ResourceLoader.LoadTextResourceOrFile(ManifestFileName);
            var rows = JsonSerializer.Deserialize<List<ParameterManifestRowDto>>(json, JsonOptions) ?? new List<ParameterManifestRowDto>();

            var parsed = new List<ParameterBindingManifestEntry>();
            var errors = new List<string>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                try
                {
                    parsed.Add(new ParameterBindingManifestEntry
                    {
                        Name = row?.name,
                        Scope = ParseScope(row?.scope),
                        RequiredForSetupCheck = row?.requiredForSetupCheck == true,
                        Usage = row?.usage,
                        Aliases = row?.aliases ?? Array.Empty<string>(),
                        Categories = row?.categories ?? Array.Empty<string>()
                    });
                }
                catch (Exception ex)
                {
                    var rowName = string.IsNullOrWhiteSpace(row?.name) ? "<unnamed>" : row.name;
                    errors.Add($"Manifest row {i} ('{rowName}') failed: {ex.Message}");
                }
            }

            _cache = parsed;
            _diagnostics = new ParameterManifestLoadDiagnostics
            {
                TotalRows = rows.Count,
                ParsedRows = parsed.Count,
                Errors = errors
            };
            return _cache.ToList();
        }

        public static ParameterBindingManifestEntry FindByName(string parameterName)
        {
            return All().FirstOrDefault(x =>
                string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase) ||
                (x.Aliases ?? Array.Empty<string>()).Any(a => string.Equals(a, parameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private static ParameterScopeKind ParseScope(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new Exception("Scope is missing.");

            switch (value.Trim().ToLowerInvariant())
            {
                case "instance": return ParameterScopeKind.Instance;
                case "type": return ParameterScopeKind.Type;
                case "project": return ParameterScopeKind.Project;
                default:
                    throw new Exception($"Invalid scope '{value}'. Expected Instance, Type, or Project.");
            }
        }
    }
}
