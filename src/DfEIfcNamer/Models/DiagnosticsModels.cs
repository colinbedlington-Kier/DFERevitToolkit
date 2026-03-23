using System;
using System.Collections.Generic;

namespace DfEIfcNamer.Models
{
    public enum DiagnosticLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public class DiagnosticLogEntry
    {
        public DateTime Timestamp { get; set; }
        public DiagnosticLevel Level { get; set; }
        public string Stage { get; set; }
        public string Message { get; set; }
        public string ExceptionType { get; set; }
        public string StackTrace { get; set; }
        public string InnerException { get; set; }
        public string ContextJson { get; set; }
    }

    public class DiagnosticsSummary
    {
        public string DocumentTitle { get; set; }
        public string RevitVersion { get; set; }
        public string ActiveProjectName { get; set; }
        public string SharedParameterPath { get; set; }
        public bool SharedParameterFileExists { get; set; }
        public bool OpenSharedParameterFileSucceeded { get; set; }
        public int GroupCount { get; set; }
        public int DefinitionCount { get; set; }
        public DateTime? LastRunTimeUtc { get; set; }
        public int TotalExpectedParameters { get; set; }
        public int TotalParametersFound { get; set; }
        public int TotalInsertSuccesses { get; set; }
        public int TotalReInsertSuccesses { get; set; }
        public int TotalVerified { get; set; }
        public int IfcClassesLoaded { get; set; }
        public int IfcPredefinedTypesLoaded { get; set; }
        public int InvalidIfcMetadataCount { get; set; }
        public string LastErrorSummary { get; set; }
        public string LastSuccessfulParameterFound { get; set; }
        public string LastSuccessfulBinding { get; set; }
        public string LastFailedBinding { get; set; }
    }

    public class DiagnosticsState
    {
        public DiagnosticsSummary Summary { get; set; } = new DiagnosticsSummary();
        public string PlainTextLog { get; set; }
        public IList<DiagnosticLogEntry> Entries { get; set; } = new List<DiagnosticLogEntry>();
    }
}
