using System;
using System.Collections.Generic;
using System.Linq;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class NamingCodeRegistryService
    {
        private List<NamingCodeMapEntry> _entries = new List<NamingCodeMapEntry>();

        public void SetEntries(IEnumerable<NamingCodeMapEntry> entries)
        {
            _entries = entries?.Where(e => !string.IsNullOrWhiteSpace(e.IfcClass) && !string.IsNullOrWhiteSpace(e.Code))
                .ToList() ?? new List<NamingCodeMapEntry>();
        }

        public IList<NamingCodeMapEntry> GetEntries() => _entries.ToList();

        public string ResolveCode(string ifcClass, string predefinedType)
        {
            var cls = Normalize(ifcClass);
            var predef = Normalize(predefinedType);

            var exact = _entries.FirstOrDefault(e => Normalize(e.IfcClass) == cls && Normalize(e.PredefinedType) == predef);
            if (exact != null) return exact.Code;

            var classOnly = _entries.FirstOrDefault(e => Normalize(e.IfcClass) == cls && string.IsNullOrWhiteSpace(e.PredefinedType));
            return classOnly?.Code;
        }

        private static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
