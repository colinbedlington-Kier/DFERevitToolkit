using System;
using System.Collections.Generic;
using System.Linq;

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
        public string[] Aliases { get; set; } = Array.Empty<string>();
    }

    public static class ParameterBindingManifest
    {
        private static readonly IList<ParameterBindingManifestEntry> Entries = new List<ParameterBindingManifestEntry>
        {
            Entry("IFCName", ParameterScopeKind.Instance, true, "IfcName"),
            Entry("IfcDescription", ParameterScopeKind.Instance, true),
            Entry("IFCName [Type]", ParameterScopeKind.Type, true, "IFCName[Type]", "IfcName[Type]"),
            Entry("IfcDescription[Type]", ParameterScopeKind.Type, true),
            Entry("SystemName", ParameterScopeKind.Instance, true),
            Entry("SystemDescription", ParameterScopeKind.Instance, true),
            Entry("SystemCategory", ParameterScopeKind.Instance, true),
            Entry("ZoneName", ParameterScopeKind.Instance, true),
            Entry("ZoneDescription", ParameterScopeKind.Instance, true),
            Entry("ZoneCategory", ParameterScopeKind.Instance, true),
            Entry("SpaceReference", ParameterScopeKind.Instance, true),
            Entry("DfE ADS Classification", ParameterScopeKind.Instance, true),
            Entry("Classification", ParameterScopeKind.Type, true),
            Entry("Classification(2)", ParameterScopeKind.Type, true),
            Entry("Classification(3)", ParameterScopeKind.Type, false),
            Entry("Classification(4)", ParameterScopeKind.Type, false),
            Entry("Classification(5)", ParameterScopeKind.Type, false),
            Entry("Classification(6)", ParameterScopeKind.Type, false),
            Entry("Classification(7)", ParameterScopeKind.Type, false),
            Entry("Classification(8)", ParameterScopeKind.Type, false),
            Entry("Classification(9)", ParameterScopeKind.Type, false),
            Entry("Classification.Uniclass.Pr.Number", ParameterScopeKind.Type, true),
            Entry("Classification.Uniclass.Pr.Description", ParameterScopeKind.Type, true),
            Entry("Classification.Uniclass.Ss.Number", ParameterScopeKind.Instance, true),
            Entry("Classification.Uniclass.Ss.Description", ParameterScopeKind.Instance, true),
            Entry("IfcProjectName", ParameterScopeKind.Project, true),
            Entry("IfcProjectDescription", ParameterScopeKind.Project, true),
            Entry("IfcSiteName", ParameterScopeKind.Project, true),
            Entry("IfcSiteDescription", ParameterScopeKind.Project, true),
            Entry("IfcBuildingName", ParameterScopeKind.Project, true),
            Entry("IfcBuildingDescription", ParameterScopeKind.Project, true),
            Entry("UPRN", ParameterScopeKind.Project, true),
            Entry("MaximumBlockHeight", ParameterScopeKind.Project, true),
            Entry("DfE_ProjectInfoJson", ParameterScopeKind.Project, false),
            Entry("DfE_NamingCounters", ParameterScopeKind.Project, false),
            Entry("DfE_IFCPredefinedType", ParameterScopeKind.Instance, false),
            Entry("DfE_UserDefinedPredefinedTypeValue", ParameterScopeKind.Instance, false),
            Entry("DfE_IFCEntity", ParameterScopeKind.Instance, false)
        };

        public static IList<ParameterBindingManifestEntry> All() => Entries.ToList();

        public static ParameterBindingManifestEntry FindByName(string parameterName)
        {
            return Entries.FirstOrDefault(x =>
                string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase) ||
                x.Aliases.Any(a => string.Equals(a, parameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private static ParameterBindingManifestEntry Entry(string name, ParameterScopeKind scope, bool required, params string[] aliases)
        {
            return new ParameterBindingManifestEntry { Name = name, Scope = scope, RequiredForSetupCheck = required, Aliases = aliases ?? Array.Empty<string>() };
        }
    }
}
