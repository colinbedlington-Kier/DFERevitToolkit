using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class DiagnosticsCollectorService
    {
        private readonly object _sync = new object();
        private readonly List<DiagnosticLogEntry> _entries = new List<DiagnosticLogEntry>();

        public DiagnosticsSummary Summary { get; } = new DiagnosticsSummary();

        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
            }

            Summary.LastRunTimeUtc = DateTime.UtcNow;
            Summary.LastErrorSummary = string.Empty;
            Summary.LastSuccessfulParameterFound = string.Empty;
            Summary.LastSuccessfulBinding = string.Empty;
            Summary.LastFailedBinding = string.Empty;
        }

        public void AddInfo(string stage, string message, object context = null) => Add(DiagnosticLevel.Info, stage, message, null, context);
        public void AddWarning(string stage, string message, object context = null) => Add(DiagnosticLevel.Warning, stage, message, null, context);
        public void AddDebug(string stage, string message, object context = null) => Add(DiagnosticLevel.Debug, stage, message, null, context);
        public void AddError(string stage, string message, Exception ex = null, object context = null) => Add(DiagnosticLevel.Error, stage, message, ex, context);

        public void Add(DiagnosticLevel level, string stage, string message, Exception ex = null, object context = null)
        {
            var entry = new DiagnosticLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Stage = stage ?? "General",
                Message = message ?? string.Empty,
                ExceptionType = ex?.GetType().FullName,
                StackTrace = ex?.StackTrace,
                InnerException = ex?.InnerException?.ToString(),
                ContextJson = context == null ? null : Serialize(context)
            };

            lock (_sync)
            {
                _entries.Add(entry);
            }

            if (level == DiagnosticLevel.Error)
            {
                Summary.LastErrorSummary = message;
            }
        }

        public DiagnosticsState Snapshot()
        {
            List<DiagnosticLogEntry> entries;
            lock (_sync)
            {
                entries = _entries.ToList();
            }

            return new DiagnosticsState
            {
                Entries = entries,
                PlainTextLog = BuildPlainText(entries),
                Summary = Summary
            };
        }

        public string BuildCopyPayload()
        {
            var state = Snapshot();
            var summary = state.Summary;
            var header = new StringBuilder();
            header.AppendLine("=== DfE IFC Namer Diagnostics Summary ===");
            header.AppendLine("Document title: " + (summary.DocumentTitle ?? "n/a"));
            header.AppendLine("Revit version: " + (summary.RevitVersion ?? "n/a"));
            header.AppendLine("Active project name: " + (summary.ActiveProjectName ?? "n/a"));
            header.AppendLine("Shared parameter path: " + (summary.SharedParameterPath ?? "n/a"));
            header.AppendLine("Total groups: " + summary.GroupCount);
            header.AppendLine("Total definitions: " + summary.DefinitionCount);
            header.AppendLine("Total expected parameters: " + summary.TotalExpectedParameters);
            header.AppendLine("Total parameters found: " + summary.TotalParametersFound);
            header.AppendLine("Total insert successes: " + summary.TotalInsertSuccesses);
            header.AppendLine("Total reinsert successes: " + summary.TotalReInsertSuccesses);
            header.AppendLine("Total verified: " + summary.TotalVerified);
            header.AppendLine("========================================");
            header.AppendLine();
            header.Append(state.PlainTextLog);
            return header.ToString();
        }

        public string ExportTxt(string outputFolder)
        {
            var file = Path.Combine(outputFolder, "DfEIfcNamer_Diagnostics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(file, BuildCopyPayload(), Encoding.UTF8);
            return file;
        }

        public string ExportJson(string outputFolder)
        {
            var file = Path.Combine(outputFolder, "DfEIfcNamer_Diagnostics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
            var payload = Snapshot();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json, Encoding.UTF8);
            return file;
        }

        private static string BuildPlainText(IList<DiagnosticLogEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.Append('[').Append(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                    .Append('[').Append(entry.Level.ToString().ToUpperInvariant()).Append("] ")
                    .Append('[').Append(entry.Stage).Append("] ")
                    .Append(entry.Message)
                    .AppendLine();

                if (!string.IsNullOrWhiteSpace(entry.ExceptionType))
                {
                    sb.AppendLine("  ExceptionType: " + entry.ExceptionType);
                }

                if (!string.IsNullOrWhiteSpace(entry.InnerException))
                {
                    sb.AppendLine("  InnerException: " + entry.InnerException);
                }

                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    sb.AppendLine("  StackTrace: " + entry.StackTrace);
                }

                if (!string.IsNullOrWhiteSpace(entry.ContextJson))
                {
                    sb.AppendLine("  Context: " + entry.ContextJson);
                }
            }

            return sb.ToString();
        }

        private static string Serialize(object payload)
        {
            try
            {
                return JsonSerializer.Serialize(payload);
            }
            catch
            {
                return payload.ToString();
            }
        }
    }
}
