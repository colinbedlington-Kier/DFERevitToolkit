using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DfEIfcNamer.Models
{
    public abstract class ObservableModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

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
        public string NamingCodesSource { get; set; }
        public string SystemsSource { get; set; }
        public string ManifestSource { get; set; }
        public string SharedParameterSource { get; set; }
        public bool ManifestLoaded { get; set; }
        public bool SharedParameterFileLoaded { get; set; }
        public int ManifestEntriesCount { get; set; }
        public int ManifestTotalRowsCount { get; set; }
        public int ManifestParsedRowsCount { get; set; }
        public int ManifestFailedRowsCount { get; set; }
        public int SharedParameterDefinitionsCount { get; set; }
        public int MatchedSharedParameterDefinitionsCount { get; set; }
        public int ProjectedRowsCount { get; set; }
        public IList<string> RowLevelErrors { get; set; } = new List<string>();
        public IList<string> Exceptions { get; set; } = new List<string>();
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
        public string MatchedPrefix { get; set; }
        public List<string> AllowedCategories { get; set; } = new List<string>();
        public List<string> AllowedIfcClasses { get; set; } = new List<string>();
        public List<string> AllowedCategoryPrefixes { get; set; } = new List<string>();
    }

    public class NamingPreviewRow : ObservableModel
    {
        private bool _isSelected = true;
        private string _proposedIfcPredefinedType;
        private string _proposedUserDefinedPredefinedType;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
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
        public string ProposedSystemCategory { get; set; }
        public string SourceSsNumber { get; set; }
        public string SourceSsDescription { get; set; }
        public string MatchedSystemPrefix { get; set; }
        public bool IsUserDefinedSystem { get; set; }
        public string UserDefinedValidationError { get; set; }
        public string CandidateSystems { get; set; }
        public string ProposedIfcExportAs { get; set; }
        public string ProposedIfcEntity { get; set; }
        public string ProposedIfcPredefinedType { get => _proposedIfcPredefinedType; set => SetField(ref _proposedIfcPredefinedType, value); }
        public string ProposedUserDefinedPredefinedType { get => _proposedUserDefinedPredefinedType; set => SetField(ref _proposedUserDefinedPredefinedType, value); }
        public ObservableCollection<string> AllowedIfcPredefinedTypes { get; set; } = new ObservableCollection<string>();
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
        public int ResolvedIfcEntityCount { get; set; }
        public int ResolvedPredefinedTypeCount { get; set; }
        public int UserDefinedFallbackCount { get; set; }
        public int UnresolvedCount { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
    }


    public class ParameterWriteReportRow
    {
        public string Scope { get; set; }
        public string Target { get; set; }
        public string Parameter { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
    }

    public class ApplyResult
    {
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int UniqueTypesUpdated { get; set; }
        public int InstancesUpdated { get; set; }
        public int ExportAsUpdated { get; set; }
        public int AdsClassificationUpdated { get; set; }
        public int AdsTextUpdated { get; set; }
        public int CobieComponentUpdated { get; set; }
        public int CobieTypeUpdated { get; set; }
        public IList<string> Logs { get; set; } = new List<string>();
        public IList<ParameterWriteReportRow> ReportRows { get; set; } = new List<ParameterWriteReportRow>();
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
        public string NumberOfStoreys { get; set; }
        public string Phase { get; set; }
        public string BlockConstructionType { get; set; }
    }

    public class HeaderValidationResult
    {
        public bool IsValid { get; set; }
        public IList<string> Messages { get; set; } = new List<string>();
    }

    public class SpaceZonePreviewRow : ObservableModel
    {
        private bool _isSelected = true;
        private string _proposedZoneName;
        private string _proposedAdsClassification;
        private string _proposedAdsText;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public long ElementId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string FamilyType { get; set; }
        public string Level { get; set; }
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public string CurrentSpaceReference { get; set; }
        public string ProposedSpaceReference { get; set; }
        public string CurrentZoneName { get; set; }
        public string ProposedZoneName { get => _proposedZoneName; set => SetField(ref _proposedZoneName, value); }
        public string CurrentZoneDescription { get; set; }
        public string ProposedZoneDescription { get; set; }
        public string CurrentZoneCategory { get; set; }
        public string ProposedZoneCategory { get; set; }
        public string CurrentAdsClassification { get; set; }
        public string ProposedAdsClassification { get => _proposedAdsClassification; set => SetField(ref _proposedAdsClassification, value); }
        public string CurrentAdsText { get; set; }
        public string ProposedAdsText { get => _proposedAdsText; set => SetField(ref _proposedAdsText, value); }
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
        public bool AddAsNewSystem { get; set; } = true;
        public bool AppendToExistingSystem { get; set; }
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
        public string DisplayText => string.IsNullOrWhiteSpace(Description) ? Code : $"{Code} : {Description}";
    }

    public class ClassificationSyncPreviewRow
    {
        public long ElementId { get; set; }
        public long TypeElementId { get; set; }
        public string Category { get; set; }
        public string SourceClassification { get; set; }
        public string SourceClassification2 { get; set; }
        public string SourceClassificationEnName { get; set; }
        public string SourceClassificationEfName { get; set; }
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
        public int SelectedCount { get; set; }
        public int ClassifiedCount { get; set; }
        public int MissingClassificationCount { get; set; }
        public int SourceRows { get; set; }
        public int TypeTargets { get; set; }
        public int InstanceTargets { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
    }

    public class SystemCandidateOption
    {
        public string SystemName { get; set; }
        public string MatchedPrefix { get; set; }
        public int MatchLength { get; set; }
        public string DisplayText => string.IsNullOrWhiteSpace(MatchedPrefix) ? SystemName : $"{SystemName} ({MatchedPrefix})";
    }

    public class ComplianceCheckResult : ObservableModel
    {
        private bool _isSelected = true;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public long ElementId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string Level { get; set; }
        public string CurrentIfcName { get; set; }
        public string CurrentIfcTypeName { get; set; }
        public string CurrentIfcEntity { get; set; }
        public string CurrentIfcPredefinedType { get; set; }
        public string CurrentAdsClassification { get; set; }
        public string CurrentSystemName { get; set; }
        public string CurrentZoneName { get; set; }
        public string RuleGroup { get; set; }
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string ExpectedRequirement { get; set; }
        public string ActualValue { get; set; }
        public string Status { get; set; }
        public string Severity { get; set; }
        public string Notes { get; set; }
        public bool IsApplicable { get; set; } = true;
        public bool IsFailed => string.Equals(Status, "Fail", StringComparison.OrdinalIgnoreCase);
        public bool IsCompliant => string.Equals(Status, "Pass", StringComparison.OrdinalIgnoreCase);
        public bool HighlightIfcName { get; set; }
        public bool HighlightIfcTypeName { get; set; }
        public bool HighlightIfcEntity { get; set; }
        public bool HighlightIfcPredefinedType { get; set; }
        public bool HighlightAdsClassification { get; set; }
        public bool HighlightSystemName { get; set; }
        public bool HighlightZoneName { get; set; }
        public bool HighlightActualValue => IsFailed;
    }

    public class ComplianceRunSummary
    {
        public IList<ComplianceCheckResult> Rows { get; set; } = new List<ComplianceCheckResult>();
        public int TotalElementsChecked { get; set; }
        public int CompliantElementsCount { get; set; }
        public int NonCompliantElementsCount { get; set; }
        public int TotalApplicableChecks { get; set; }
        public int PassedApplicableChecks { get; set; }
        public int FailedApplicableChecks { get; set; }
        public double ElementCompliancePercent { get; set; }
        public double RuleCompliancePercent { get; set; }
        public string MetricDefinition { get; set; }
        public IList<string> RuleGroups { get; set; } = new List<string>();
    }
}
