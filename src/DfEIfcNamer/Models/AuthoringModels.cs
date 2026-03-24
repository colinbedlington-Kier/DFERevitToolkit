using System;
using System.Collections.Generic;

namespace DfEIfcNamer.Models
{
    public enum NamingScopeMode
    {
        CurrentSelection,
        AllInstancesOfSelectedTypes,
        SelectedCategories,
        CurrentView,
        WholeModelByCategory
    }

    public enum InstanceNumberingMode
    {
        Sequential,
        ElementId
    }

    public class RequiredParameterStatus
    {
        public string ParameterName { get; set; }
        public string Scope { get; set; }
        public bool Exists { get; set; }
        public bool Writable { get; set; }
        public string Notes { get; set; }
    }

    public class SetupCheckResult
    {
        public string Status { get; set; }
        public string Notes { get; set; }
        public IList<RequiredParameterStatus> Parameters { get; set; } = new List<RequiredParameterStatus>();
        public bool NamingMapLoaded { get; set; }
        public bool SystemListLoaded { get; set; }
    }

    public class NamingCodeMapEntry
    {
        public string IfcClass { get; set; }
        public string PredefinedType { get; set; }
        public string Code { get; set; }
    }

    public class SystemRegistryEntry
    {
        public string SystemName { get; set; }
        public string SystemDescription { get; set; }
        public string Discipline { get; set; }
        public List<string> AllowedCategories { get; set; } = new List<string>();
        public List<string> AllowedIfcClasses { get; set; } = new List<string>();
    }

    public class NamingPreviewRow
    {
        public long ElementId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string Level { get; set; }
        public string CurrentIfcName { get; set; }
        public string ProposedIfcName { get; set; }
        public string CurrentIfcTypeName { get; set; }
        public string ProposedIfcTypeName { get; set; }
        public string CurrentSystemName { get; set; }
        public string ProposedSystemName { get; set; }
        public string ProposedSystemDescription { get; set; }
        public string Status { get; set; }
        public bool Eligible { get; set; }
    }

    public class NamingPreviewResult
    {
        public IList<NamingPreviewRow> Rows { get; set; } = new List<NamingPreviewRow>();
        public int SelectedCount { get; set; }
        public int EligibleCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
    }

    public class ApplyResult
    {
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public IList<string> Logs { get; set; } = new List<string>();
    }

    public class HeaderDataModel
    {
        public string IfcProjectName { get; set; }
        public string IfcProjectDescription { get; set; }
        public string IfcSiteName { get; set; }
        public string IfcSiteDescription { get; set; }
        public string IfcBuildingName { get; set; }
        public string IfcBuildingDescription { get; set; }
        public string UPRN { get; set; }
        public string MaximumBlockHeight { get; set; }
    }

    public class HeaderValidationResult
    {
        public bool IsValid { get; set; }
        public IList<string> Messages { get; set; } = new List<string>();
    }

    public class SpaceZonePreviewRow
    {
        public long ElementId { get; set; }
        public string Category { get; set; }
        public string FamilyType { get; set; }
        public string Level { get; set; }
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public string CurrentSpaceReference { get; set; }
        public string ProposedSpaceReference { get; set; }
        public string CurrentZoneName { get; set; }
        public string ProposedZoneName { get; set; }
        public string Status { get; set; }
    }

    public class SpaceZonePreviewResult
    {
        public IList<SpaceZonePreviewRow> Rows { get; set; } = new List<SpaceZonePreviewRow>();
        public int SelectedCount { get; set; }
        public int MissingRoomCount { get; set; }
    }

    public class ValidationSummary
    {
        public string SetupReadiness { get; set; }
        public string NamingCompleteness { get; set; }
        public string HeaderCompleteness { get; set; }
        public string SpaceZoneCompleteness { get; set; }
        public IList<string> Messages { get; set; } = new List<string>();
    }

    public class ToolConfigModel
    {
        public List<NamingCodeMapEntry> NamingCodes { get; set; } = new List<NamingCodeMapEntry>();
        public List<SystemRegistryEntry> Systems { get; set; } = new List<SystemRegistryEntry>();
        public HeaderDataModel HeaderDefaults { get; set; } = new HeaderDataModel();
    }

    public class NamingGenerationRequest
    {
        public NamingScopeMode ScopeMode { get; set; }
        public List<long> CategoryIds { get; set; } = new List<long>();
        public bool UseFallbackCode { get; set; }
        public string FallbackCode { get; set; } = "UNM";
        public int TypeNumberWidth { get; set; } = 2;
        public string FallbackPredefinedType { get; set; } = "Undefined";
        public InstanceNumberingMode InstanceNumberingMode { get; set; }
        public string SelectedSystemName { get; set; }
        public bool AllowDoorWindowUnassignedFallback { get; set; }
        public string UnassignedRoomPrefix { get; set; } = "UNASSIGNED";
    }

    public class SpaceZoneRequest
    {
        public List<long> ElementIds { get; set; } = new List<long>();
        public string ProposedZoneName { get; set; }
    }
}
