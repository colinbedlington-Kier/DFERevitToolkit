using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public enum ParameterScope
    {
        Instance,
        Type,
        Room,
        Project,
        Unknown
    }

    public static class ParameterScopeMap
    {
        private static readonly HashSet<string> TypeParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "COBie.Type.Name",
            "IFCName [Type]",
            "IFCName[Type]",
            "IfcName [Type]",
            "IfcName[Type]",
            "IfcDescription [Type]",
            "IfcDescription[Type]",
            "Export to IFC As",
            "IFC Predefined Type",
            "Classification(6)",
            "Classification(7)",
            "Classification(8)",
            "Classification(9)"
        };

        private static readonly HashSet<string> RoomParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DfE ADS Classification",
            "RoomTag",
            "SpaceReference",
            "ZoneName",
            "ZoneCategory",
            "ZoneDescription"
        };

        private static readonly HashSet<string> ProjectParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NumberOfStoreys",
            "Phase",
            "BlockConstructionType"
        };

        public static ParameterScope Resolve(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return ParameterScope.Unknown;
            if (TypeParameters.Contains(parameterName)) return ParameterScope.Type;
            if (RoomParameters.Contains(parameterName)) return ParameterScope.Room;
            if (ProjectParameters.Contains(parameterName)) return ParameterScope.Project;
            return ParameterScope.Instance;
        }
    }

    public class ParameterWriteService
    {
        private readonly ParameterAliasResolver _aliasResolver = new ParameterAliasResolver();

        public bool SetInstanceParameter(Element element, string name, string value, ApplyResult result = null)
        {
            if (element == null) return LogFailure(result, "Instance", "n/a", name, "missing element");
            var match = _aliasResolver.Resolve(element, name);
            var parameter = match.Parameter;
            if (parameter == null) return LogFailure(result, "Instance", element.Id.Value.ToString(), name, "missing parameter");
            if (parameter.IsReadOnly) return LogFailure(result, "Instance", element.Id.Value.ToString(), name, "read-only parameter");
            if (value == null) return LogSkipped(result, "Instance", element.Id.Value.ToString(), name, "null value");

            parameter.Set(value);
            LogSuccess(result, "Instance", element.Id.Value.ToString(), name, match.MatchedName, match.AliasMatched);
            return true;
        }

        public bool SetTypeParameter(Document doc, Element element, string name, string value, ApplyResult result = null)
        {
            if (element == null) return LogFailure(result, "Type", "n/a", name, "missing element");
            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId) return LogFailure(result, "Type", element.Id.Value.ToString(), name, "missing type id");

            var type = doc.GetElement(typeId);
            if (type == null) return LogFailure(result, "Type", element.Id.Value.ToString(), name, "missing type element");
            var match = _aliasResolver.Resolve(type, name);
            var parameter = match.Parameter;
            if (parameter == null) return LogFailure(result, "Type", type.Id.Value.ToString(), name, "missing parameter");
            if (parameter.IsReadOnly) return LogFailure(result, "Type", type.Id.Value.ToString(), name, "read-only parameter");
            if (value == null) return LogSkipped(result, "Type", type.Id.Value.ToString(), name, "null value");

            parameter.Set(value);
            LogSuccess(result, "Type", type.Id.Value.ToString(), name, match.MatchedName, match.AliasMatched);
            return true;
        }

        public bool SetProjectParameter(Document doc, string name, string value, ApplyResult result = null)
        {
            if (doc?.ProjectInformation == null) return LogFailure(result, "Project", "Project", name, "missing project information");
            var parameter = doc.ProjectInformation.LookupParameter(name);
            if (parameter == null) return LogFailure(result, "Project", "Project", name, "missing parameter");
            if (parameter.IsReadOnly) return LogFailure(result, "Project", "Project", name, "read-only parameter");
            if (value == null) return LogSkipped(result, "Project", "Project", name, "null value");

            parameter.Set(value);
            LogSuccess(result, "Project", "Project", name);
            return true;
        }

        public bool SetRoomParameter(Element element, string name, string value, ApplyResult result = null)
        {
            if (element == null) return LogFailure(result, "Room", "n/a", name, "missing element");
            if (!IsRoom(element)) return LogSkipped(result, "Room", element.Id.Value.ToString(), name, "wrong category");

            var parameter = element.LookupParameter(name);
            if (parameter == null) return LogFailure(result, "Room", element.Id.Value.ToString(), name, "missing parameter");
            if (parameter.IsReadOnly) return LogFailure(result, "Room", element.Id.Value.ToString(), name, "read-only parameter");
            if (value == null) return LogSkipped(result, "Room", element.Id.Value.ToString(), name, "null value");

            parameter.Set(value);
            LogSuccess(result, "Room", element.Id.Value.ToString(), name);
            return true;
        }

        public static bool IsRoom(Element element)
        {
            return element?.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_Rooms;
        }

        private static bool LogFailure(ApplyResult result, string scope, string target, string parameter, string reason)
        {
            result?.Logs.Add($"Scope={scope}; Target={target}; Parameter={parameter}; Status=Failed; Reason={reason}");
            result?.ReportRows.Add(new ParameterWriteReportRow { Scope = scope, Target = target, Parameter = parameter, Status = "Failed", Reason = reason });
            if (result != null) result.Failed++;
            return false;
        }

        private static bool LogSkipped(ApplyResult result, string scope, string target, string parameter, string reason)
        {
            result?.Logs.Add($"Scope={scope}; Target={target}; Parameter={parameter}; Status=Skipped; Reason={reason}");
            result?.ReportRows.Add(new ParameterWriteReportRow { Scope = scope, Target = target, Parameter = parameter, Status = "Skipped", Reason = reason });
            if (result != null) result.Skipped++;
            return false;
        }

        private static void LogSuccess(ApplyResult result, string scope, string target, string parameter, string matchedParameter, bool aliasMatched)
        {
            var detail = string.IsNullOrWhiteSpace(matchedParameter)
                ? string.Empty
                : (aliasMatched ? $"alias matched: {matchedParameter}" : $"matched: {matchedParameter}");
            result?.Logs.Add($"Scope={scope}; Target={target}; Parameter={parameter}; Status=Success; {detail}");
            result?.ReportRows.Add(new ParameterWriteReportRow { Scope = scope, Target = target, Parameter = parameter, Status = "Success", Reason = detail });
        }
    }
}
