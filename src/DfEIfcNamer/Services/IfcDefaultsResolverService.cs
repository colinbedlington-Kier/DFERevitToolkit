using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DfEIfcNamer.Services
{
    public class IfcDefaultsResolverService
    {
        private const string PredefinedTypesFileName = "DfeIfc2x3PredefinedTypes.json";
        private readonly ResourceFileLoader _resourceLoader = new ResourceFileLoader();
        private readonly Dictionary<string, List<string>> _predefinedByEntity = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public IfcDefaultsResolverService()
        {
            LoadPredefinedTypes();
        }

        public int PredefinedTypesCount => _predefinedByEntity.Sum(x => x.Value.Count);
        public string PredefinedTypesSource { get; private set; } = "embedded";
        public string PredefinedTypesSourceDetail { get; private set; } = string.Empty;

        public (string Entity, string PredefinedType, string UserDefinedValue, bool Resolved) ResolveDefaults(string category, string family, string type)
        {
            var entity = ResolveEntity(category, family, type);
            if (string.IsNullOrWhiteSpace(entity))
            {
                return (string.Empty, string.Empty, string.Empty, false);
            }

            var predefined = ResolvePredefined(entity);
            return (entity, predefined, string.Empty, !string.IsNullOrWhiteSpace(predefined));
        }

        public IList<string> GetAllowedPredefinedTypes(string entity)
        {
            var normalized = NormalizeEntity(entity);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<string> { "USERDEFINED" };
            }

            if (!_predefinedByEntity.TryGetValue(normalized, out var list) || list.Count == 0)
            {
                return new List<string> { "USERDEFINED" };
            }

            return list
                .Select(x => x?.Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void LoadPredefinedTypes()
        {
            var explicitPath = _resourceLoader.ResolveExternalResourcePath(PredefinedTypesFileName);
            if (System.IO.File.Exists(explicitPath))
            {
                PredefinedTypesSource = "external";
                PredefinedTypesSourceDetail = explicitPath;
            }
            else
            {
                var embeddedName = _resourceLoader.ResolveEmbeddedResourceName(PredefinedTypesFileName);
                PredefinedTypesSource = "embedded";
                PredefinedTypesSourceDetail = embeddedName ?? "<missing>";
            }

            var records = _resourceLoader.LoadJsonResourceOrFile<List<IfcPredefinedTypeRecord>>(PredefinedTypesFileName, explicitPath, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<IfcPredefinedTypeRecord>();

            foreach (var record in records.Where(r => !string.IsNullOrWhiteSpace(r.Entity) && !string.IsNullOrWhiteSpace(r.Value)))
            {
                var key = NormalizeEntity(record.Entity);
                if (!_predefinedByEntity.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    _predefinedByEntity[key] = list;
                }

                if (!list.Any(x => string.Equals(x, record.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(record.Value.ToUpperInvariant());
                }
            }
        }

        private static string NormalizeEntity(string entity)
        {
            if (string.IsNullOrWhiteSpace(entity))
            {
                return string.Empty;
            }

            var value = entity.Trim();
            if (value.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(3);
            }

            return value;
        }

        private string ResolveEntity(string category, string family, string type)
        {
            var hint = $"{category} {family} {type}".ToLowerInvariant();
            if (hint.Contains("wall")) return "Wall";
            if (hint.Contains("door")) return "Door";
            if (hint.Contains("window")) return "Window";
            if (hint.Contains("beam") || hint.Contains("structural framing")) return "Beam";
            if (hint.Contains("furniture")) return "Furniture";
            return string.Empty;
        }

        private string ResolvePredefined(string entity)
        {
            if (!_predefinedByEntity.TryGetValue(entity, out var list) || list.Count == 0)
            {
                return "USERDEFINED";
            }

            var preferred = entity.Equals("Wall", StringComparison.OrdinalIgnoreCase) ? "STANDARD"
                : entity.Equals("Door", StringComparison.OrdinalIgnoreCase) ? "DOOR"
                : entity.Equals("Window", StringComparison.OrdinalIgnoreCase) ? "WINDOW"
                : entity.Equals("Beam", StringComparison.OrdinalIgnoreCase) ? "BEAM"
                : null;

            if (!string.IsNullOrWhiteSpace(preferred) && list.Any(x => string.Equals(x, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return preferred;
            }

            var firstValid = list.FirstOrDefault(x => !x.Equals("NOTDEFINED", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(firstValid) ? "USERDEFINED" : firstValid.ToUpperInvariant();
        }

        private class IfcPredefinedTypeRecord
        {
            public string Entity { get; set; }
            public string Value { get; set; }
        }
    }
}
