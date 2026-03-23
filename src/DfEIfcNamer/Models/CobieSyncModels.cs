using System;
using System.Collections.Generic;

namespace DfEIfcNamer.Models
{
    public enum SyncScope
    {
        ActiveView,
        EntireModel
    }

    public enum OverwriteMode
    {
        BlankOnly,
        OverwriteAlways
    }

    public class SetupStatus
    {
        public string ResolvedAddinFolder { get; set; }
        public string SharedParameterFilePath { get; set; }
        public string EntityMappingJsonPath { get; set; }
        public string ClassificationSlotsJsonPath { get; set; }
        public bool SharedParameterFileFound { get; set; }
        public bool EntityMappingFileExists { get; set; }
        public bool ClassificationSlotsFileExists { get; set; }
        public bool EntityMappingLoaded { get; set; }
        public bool ClassificationSlotsLoaded { get; set; }
        public bool InstanceParameterBound { get; set; }
        public bool TypeParameterBound { get; set; }
        public int IncludedCategoriesCount { get; set; }
        public int SkippedUnsupportedCategoriesCount { get; set; }
        public int FailedBindingInsertCount { get; set; }
        public IList<string> IncludedCategoryNames { get; set; } = new List<string>();
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
    }

    public class MappingSettings
    {
        public string InstanceSource { get; set; } = "IFCName";
        public string TypeSource { get; set; } = "IFCName [Type]";
        public string InstanceTarget { get; set; } = "COBie.Component.Name";
        public string TypeTarget { get; set; } = "COBie.Type.Name";
        public SyncScope Scope { get; set; } = SyncScope.EntireModel;
        public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.BlankOnly;
        public List<long> CategoryIds { get; set; } = new List<long>();
        public DateTime? LastSyncUtc { get; set; }
    }

    public class SyncLogEntry
    {
        public string Severity { get; set; }
        public string Message { get; set; }
    }

    public class SyncResult
    {
        public int InstancesUpdated { get; set; }
        public int InstancesSkipped { get; set; }
        public int InstancesFailed { get; set; }
        public int TypesUpdated { get; set; }
        public int TypesSkipped { get; set; }
        public int TypesFailed { get; set; }
        public IList<SyncLogEntry> Logs { get; set; } = new List<SyncLogEntry>();
    }

    public class ProjectParameterOption
    {
        public string Name { get; set; }
        public bool IsType { get; set; }
    }
}
