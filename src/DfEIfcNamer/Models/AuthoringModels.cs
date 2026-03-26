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
        public bool FoundInSharedParameterFile { get; set; }
        public string SharedParameterGroup { get; set; }
        public string ActualScope { get; set; }
        public string Result { get; set; }
        public string Action { get; set; }
        public string ExpectedCategories { get; set; }
        public string Usage { get; set; }
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
        public long TypeElementId { get; set; }
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
        public string ProposedIfcExportAs { get; set; }
        public string ProposedIfcPredefinedType { get; set; }
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
        public int UniqueTypesUpdated { get; set; }
        public int InstancesUpdated { get; set; }
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
        public string CurrentZoneDescription { get; set; }
        public string ProposedZoneDescription { get; set; }
        public string CurrentZoneCategory { get; set; }
        public string ProposedZoneCategory { get; set; }
        public string CurrentAdsClassification { get; set; }
        public string ProposedAdsClassification { get; set; }
        public string Status { get; set; }
    }

    public class SpaceZonePreviewResult
    {
        public IList<SpaceZonePreviewRow> Rows { get; set; } = new List<SpaceZonePreviewRow>();
        public int SelectedCount { get; set; }
        public int MissingRoomCount { get; set; }
        public int ValidRoomSpaceCount { get; set; }
        public int SkippedNonRoomSpaceCount { get; set; }
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
        public string ProposedAdsClassification { get; set; }
    }

    public class ZoneCatalogEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Hex { get; set; }
        public string Rgb { get; set; }
    }

    public class AdsClassificationEntry
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class ClassificationSyncPreviewRow
    {
        public long ElementId { get; set; }
        public long TypeElementId { get; set; }
        public string Category { get; set; }
        public string SourceClassification { get; set; }
        public string SourceClassification2 { get; set; }
        public string SourcePrNumber { get; set; }
        public string SourcePrDescription { get; set; }
        public string SourceSsNumber { get; set; }
        public string SourceSsDescription { get; set; }
        public string ProposedClassification2 { get; set; }
        public string ProposedClassification3 { get; set; }
        public string ProposedPrNumber { get; set; }
        public string ProposedPrDescription { get; set; }
        public string ProposedSsNumber { get; set; }
        public string ProposedSsDescription { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
    }

    public class ClassificationSyncResult
    {
        public IList<ClassificationSyncPreviewRow> Rows { get; set; } = new List<ClassificationSyncPreviewRow>();
        public int SourceRows { get; set; }
        public int TypeTargets { get; set; }
        public int InstanceTargets { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
    }
}
