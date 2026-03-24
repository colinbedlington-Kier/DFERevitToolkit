using System;
using System.Collections.Generic;
using System.Linq;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class SystemRegistryService
    {
        private List<SystemRegistryEntry> _systems = new List<SystemRegistryEntry>();

        public void SetEntries(IEnumerable<SystemRegistryEntry> systems)
        {
            _systems = systems?.Where(s => !string.IsNullOrWhiteSpace(s.SystemName)).ToList() ?? new List<SystemRegistryEntry>();
        }

        public IList<SystemRegistryEntry> GetEntries() => _systems.OrderBy(s => s.SystemName).ToList();

        public SystemRegistryEntry Find(string name)
        {
            return _systems.FirstOrDefault(s => string.Equals(s.SystemName, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsCompatible(SystemRegistryEntry system, string category, string ifcClass)
        {
            if (system == null) return false;
            if ((system.AllowedCategories == null || system.AllowedCategories.Count == 0) && (system.AllowedIfcClasses == null || system.AllowedIfcClasses.Count == 0)) return true;

            var categoryMatch = system.AllowedCategories?.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) == true;
            var classMatch = system.AllowedIfcClasses?.Any(c => string.Equals(c, ifcClass, StringComparison.OrdinalIgnoreCase)) == true;
            return categoryMatch || classMatch;
        }
    }
}
