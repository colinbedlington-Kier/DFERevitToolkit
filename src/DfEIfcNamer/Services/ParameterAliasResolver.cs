using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class ParameterAliasResolver
    {
        private static readonly string AliasMapSource = "in-code defaults";

        private static readonly Lazy<AliasMapState> AliasMapStateLazy = new Lazy<AliasMapState>(CreateAliasMapState, true);

        public ParameterMatch Resolve(Element element, string requestedName)
        {
            var key = requestedName?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return new ParameterMatch
                {
                    RequestedName = requestedName,
                    MatchedName = string.Empty,
                    AttemptedNames = Array.Empty<string>(),
                    AliasMapLoaded = AliasMapStateLazy.Value.Loaded,
                    AliasCount = AliasMapStateLazy.Value.AliasCount,
                    AliasSource = AliasMapSource
                };
            }

            try
            {
                var state = AliasMapStateLazy.Value;
                var candidates = BuildCandidates(state.Map, key);
                foreach (var candidate in candidates)
                {
                    var parameter = element?.LookupParameter(candidate);
                    if (parameter != null)
                    {
                        return new ParameterMatch
                        {
                            Parameter = parameter,
                            RequestedName = requestedName,
                            MatchedName = candidate,
                            AliasMatched = !string.Equals(key, candidate, StringComparison.Ordinal),
                            AttemptedNames = candidates,
                            AliasMapLoaded = state.Loaded,
                            AliasCount = state.AliasCount,
                            AliasSource = AliasMapSource
                        };
                    }
                }

                return new ParameterMatch
                {
                    RequestedName = requestedName,
                    MatchedName = string.Empty,
                    AttemptedNames = candidates,
                    AliasMapLoaded = state.Loaded,
                    AliasCount = state.AliasCount,
                    AliasSource = AliasMapSource
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DfEIfcNamer] ParameterAliasResolver.Resolve failed.");
                Trace.WriteLine("[DfEIfcNamer] ExceptionType=" + ex.GetType().FullName);
                Trace.WriteLine("[DfEIfcNamer] Message=" + ex.Message);
                Trace.WriteLine("[DfEIfcNamer] Inner=" + (ex.InnerException?.GetType().FullName + ": " + ex.InnerException?.Message));
                Trace.WriteLine("[DfEIfcNamer] Stack=" + ex.StackTrace);
                Trace.WriteLine("[DfEIfcNamer] AliasMapSource=" + AliasMapSource);

                return new ParameterMatch
                {
                    RequestedName = requestedName,
                    MatchedName = string.Empty,
                    AttemptedNames = new[] { key },
                    AliasMapLoaded = false,
                    AliasCount = 0,
                    AliasSource = AliasMapSource,
                    InitializationError = ex
                };
            }
        }

        private static AliasMapState CreateAliasMapState()
        {
            try
            {
                var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                AddAliasSet(map, "IfcName [Type]", "IfcName [Type]", "IfcName[Type]", "IFCName [Type]", "IFCName[Type]");
                AddAliasSet(map, "IfcDescription [Type]", "IfcDescription [Type]", "IfcDescription[Type]", "IFCDescription [Type]", "IFCDescription[Type]");

                var aliasCount = map.Values.Sum(x => x?.Length ?? 0);
                Trace.WriteLine("[DfEIfcNamer] ParameterAliasResolver initialized. Source=" + AliasMapSource + "; Keys=" + map.Count + "; Aliases=" + aliasCount);

                return new AliasMapState
                {
                    Map = map,
                    Loaded = true,
                    AliasCount = aliasCount
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DfEIfcNamer] ParameterAliasResolver initialization failed.");
                Trace.WriteLine("[DfEIfcNamer] ExceptionType=" + ex.GetType().FullName);
                Trace.WriteLine("[DfEIfcNamer] Message=" + ex.Message);
                Trace.WriteLine("[DfEIfcNamer] Inner=" + (ex.InnerException?.GetType().FullName + ": " + ex.InnerException?.Message));
                Trace.WriteLine("[DfEIfcNamer] Stack=" + ex.StackTrace);
                Trace.WriteLine("[DfEIfcNamer] AliasMapSource=" + AliasMapSource);

                return new AliasMapState
                {
                    Map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                    Loaded = false,
                    AliasCount = 0,
                    InitializationException = ex
                };
            }
        }

        private static void AddAliasSet(IDictionary<string, string[]> map, string canonical, params string[] aliases)
        {
            var filtered = (aliases ?? Array.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!filtered.Contains(canonical, StringComparer.OrdinalIgnoreCase)) filtered.Insert(0, canonical);

            var values = filtered.ToArray();
            foreach (var key in filtered)
            {
                map[key] = values;
            }
        }

        private static string[] BuildCandidates(IReadOnlyDictionary<string, string[]> map, string requestedName)
        {
            if (string.IsNullOrWhiteSpace(requestedName)) return Array.Empty<string>();

            if (map != null && map.TryGetValue(requestedName, out var aliases) && aliases != null && aliases.Length > 0)
            {
                return aliases;
            }

            return new[] { requestedName };
        }

        private class AliasMapState
        {
            public IReadOnlyDictionary<string, string[]> Map { get; set; }
            public bool Loaded { get; set; }
            public int AliasCount { get; set; }
            public Exception InitializationException { get; set; }
        }
    }

    public class ParameterMatch
    {
        public string RequestedName { get; set; }
        public string MatchedName { get; set; }
        public bool AliasMatched { get; set; }
        public Parameter Parameter { get; set; }
        public IEnumerable<string> AttemptedNames { get; set; } = Array.Empty<string>();
        public bool AliasMapLoaded { get; set; }
        public int AliasCount { get; set; }
        public string AliasSource { get; set; }
        public Exception InitializationError { get; set; }
    }
}
