using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class ParameterAliasResolver
    {
        private static readonly Dictionary<string, string[]> AliasMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "IfcName [Type]", new[] { "IfcName [Type]", "IfcName[Type]", "IFCName [Type]", "IFCName[Type]" } },
            { "IFCName [Type]", new[] { "IfcName [Type]", "IfcName[Type]", "IFCName [Type]", "IFCName[Type]" } },
            { "IfcDescription [Type]", new[] { "IfcDescription [Type]", "IfcDescription[Type]", "IFCDescription [Type]", "IFCDescription[Type]" } },
            { "IfcDescription[Type]", new[] { "IfcDescription [Type]", "IfcDescription[Type]", "IFCDescription [Type]", "IFCDescription[Type]" } }
        };

        public ParameterMatch Resolve(Element element, string requestedName)
        {
            var candidates = BuildCandidates(requestedName);
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
                        AliasMatched = !string.Equals(requestedName?.Trim(), candidate, StringComparison.Ordinal)
                    };
                }
            }

            return new ParameterMatch { RequestedName = requestedName, MatchedName = string.Empty };
        }

        private static IEnumerable<string> BuildCandidates(string requestedName)
        {
            if (string.IsNullOrWhiteSpace(requestedName)) return Enumerable.Empty<string>();
            var key = requestedName.Trim();
            if (AliasMap.TryGetValue(key, out var aliases))
            {
                return aliases;
            }

            return new[] { key };
        }
    }

    public class ParameterMatch
    {
        public string RequestedName { get; set; }
        public string MatchedName { get; set; }
        public bool AliasMatched { get; set; }
        public Parameter Parameter { get; set; }
    }
}
