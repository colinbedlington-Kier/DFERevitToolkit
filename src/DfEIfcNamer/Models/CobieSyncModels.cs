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
        public bool SharedParameterFileFound { get; set; }
        public bool InstanceParameterBound { get; set; }
        public bool TypeParameterBound { get; set; }
        public int MissingCategoryBindings { get; set; }
        public string Message { get; set; }
    }

    public class MappingSettings
    {
        public string InstanceSource { get; set; } = "IfcName";
        public string TypeSource { get; set; } = "IfcName[Type]";
        public string InstanceTarget { get; set; } = "COBie.Component.Name";
        public string TypeTarget { get; set; } = "COBie.Type.Name";
        public SyncScope Scope { get; set; } = SyncScope.EntireModel;
        public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.BlankOnly;
        public List<int> CategoryIds { get; set; } = new List<int>();
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
