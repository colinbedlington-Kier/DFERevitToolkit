using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            if (value == null) return LogSkipped(result, "Instance", element.Id.Value.ToString(), name, "null value");

            var context = ResolveParameterContext(element, name, "Instance", element.Id.Value.ToString(), result);
            return TryWriteValue(context.Parameter, value, result, "Instance", element.Id.Value.ToString(), name, context.MatchName, context.Detail);
        }

        public bool SetTypeParameter(Document doc, Element element, string name, string value, ApplyResult result = null)
        {
            if (element == null) return LogFailure(result, "Type", "n/a", name, "missing element");
            if (value == null) return LogSkipped(result, "Type", element.Id.Value.ToString(), name, "null value");

            var typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId)
            {
                return LogFailure(result, "Type", element.Id.Value.ToString(), name, "missing type id");
            }

            var type = doc?.GetElement(typeId);
            if (type == null)
            {
                return LogFailure(result, "Type", element.Id.Value.ToString(), name, "missing type element");
            }

            var context = ResolveParameterContext(type, name, "Type", type.Id.Value.ToString(), result);
            return TryWriteValue(context.Parameter, value, result, "Type", type.Id.Value.ToString(), name, context.MatchName, context.Detail);
        }

        public bool SetProjectParameter(Document doc, string name, string value, ApplyResult result = null)
        {
            if (doc?.ProjectInformation == null) return LogFailure(result, "Project", "Project", name, "missing project information");
            var parameter = doc.ProjectInformation.LookupParameter(name);
            if (parameter == null) return LogFailure(result, "Project", "Project", name, "missing parameter");
            if (parameter.IsReadOnly) return LogFailure(result, "Project", "Project", name, "read-only parameter");
            if (value == null) return LogSkipped(result, "Project", "Project", name, "null value");

            parameter.Set(value);
            var matchedName = parameter.Definition?.Name ?? name;
            LogSuccess(result, "Project", "Project", name, matchedName, !string.Equals(name, matchedName, StringComparison.Ordinal), "");
            return true;
        }

        public bool SetRoomParameter(Element element, string name, string value, ApplyResult result = null)
        {
            if (element == null) return LogFailure(result, "Room", "n/a", name, "missing element");
            if (!IsRoom(element)) return LogSkipped(result, "Room", element.Id.Value.ToString(), name, "wrong category");
            if (value == null) return LogSkipped(result, "Room", element.Id.Value.ToString(), name, "null value");

            var context = ResolveParameterContext(element, name, "Room", element.Id.Value.ToString(), result);
            return TryWriteValue(context.Parameter, value, result, "Room", element.Id.Value.ToString(), name, context.MatchName, context.Detail);
        }

        public static bool IsRoom(Element element)
        {
            return element?.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_Rooms;
        }

        private ParameterResolutionContext ResolveParameterContext(Element target, string requestedName, string scope, string targetId, ApplyResult result)
        {
            var canonical = requestedName?.Trim();
            var attempted = new List<string>();
            ParameterMatch match = null;

            try
            {
                match = _aliasResolver.Resolve(target, canonical);
                if (match.InitializationError != null)
                {
                    LogDiagnostic(result, scope, targetId, canonical,
                        "alias resolution failure", match.InitializationError,
                        $"aliasMapLoaded={match.AliasMapLoaded}; aliasCount={match.AliasCount}; source={match.AliasSource}");
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic(result, scope, targetId, canonical, "alias resolution failure", ex, "resolver threw before returning result");
            }

            if (Guid.TryParse(canonical, out var guid))
            {
                attempted.Add("guid:" + canonical);
                var guidParameter = target?.get_Parameter(guid);
                if (guidParameter != null)
                {
                    return new ParameterResolutionContext
                    {
                        Parameter = guidParameter,
                        MatchName = guidParameter.Definition?.Name ?? canonical,
                        Detail = BuildDetail(match, canonical, guidParameter.Definition?.Name ?? canonical, "shared parameter guid")
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(canonical))
            {
                attempted.Add("exact:" + canonical);
                var canonicalParameter = target.LookupParameter(canonical);
                if (canonicalParameter != null)
                {
                    return new ParameterResolutionContext
                    {
                        Parameter = canonicalParameter,
                        MatchName = canonical,
                        Detail = BuildDetail(match, canonical, canonical, "canonical name fallback")
                    };
                }
            }

            var aliasCandidates = (match?.AttemptedNames ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var alias in aliasCandidates)
            {
                attempted.Add("alias:" + alias);
                var aliased = target.LookupParameter(alias);
                if (aliased != null)
                {
                    return new ParameterResolutionContext
                    {
                        Parameter = aliased,
                        MatchName = aliased.Definition?.Name ?? alias,
                        Detail = BuildDetail(match, canonical, aliased.Definition?.Name ?? alias, "alias-resolved name")
                    };
                }
            }

            var manifestEntry = ParameterBindingManifest.FindByName(canonical);
            var manifestNames = new[] { manifestEntry?.Name }
                .Concat(manifestEntry?.Aliases ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var manifestName in manifestNames)
            {
                attempted.Add("manifest:" + manifestName);
                var manifestParameter = target.LookupParameter(manifestName);
                if (manifestParameter != null)
                {
                    return new ParameterResolutionContext
                    {
                        Parameter = manifestParameter,
                        MatchName = manifestParameter.Definition?.Name ?? manifestName,
                        Detail = BuildDetail(match, canonical, manifestParameter.Definition?.Name ?? manifestName, "canonical/manifest fallback")
                    };
                }
            }

            var exactParameter = FindByDefinitionName(target, canonical);
            if (exactParameter != null)
            {
                var existing = exactParameter.Definition?.Name ?? canonical;
                return new ParameterResolutionContext
                {
                    Parameter = exactParameter,
                    MatchName = existing,
                    Detail = BuildDetail(match, canonical, existing, "existing parameter-name fallback")
                };
            }

            var attempts = string.Join(" | ", BuildAttemptNames(match, canonical).Concat(attempted).Distinct(StringComparer.OrdinalIgnoreCase));
            LogFailure(result, scope, targetId, canonical, "parameter not found; attempts=" + attempts);

            return new ParameterResolutionContext
            {
                Parameter = null,
                MatchName = string.Empty,
                Detail = BuildDetail(match, canonical, string.Empty, "all lookups failed")
            };
        }

        private static IEnumerable<string> BuildAttemptNames(ParameterMatch match, string canonical)
        {
            var attempts = new List<string>();
            if (!string.IsNullOrWhiteSpace(match?.MatchedName)) attempts.Add("resolved:" + match.MatchedName);
            if (!string.IsNullOrWhiteSpace(canonical)) attempts.Add("canonical:" + canonical);
            if (match?.AttemptedNames != null)
            {
                attempts.AddRange(match.AttemptedNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => "alias-candidate:" + x));
            }

            return attempts.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildDetail(ParameterMatch match, string requested, string matched, string resolutionPath)
        {
            return "requested=" + (requested ?? string.Empty)
                   + "; matched=" + (matched ?? string.Empty)
                   + "; path=" + resolutionPath
                   + "; aliasMapLoaded=" + (match?.AliasMapLoaded.ToString() ?? "false")
                   + "; aliasCount=" + (match?.AliasCount.ToString(CultureInfo.InvariantCulture) ?? "0")
                   + "; aliasSource=" + (match?.AliasSource ?? "unknown");
        }

        private static Parameter FindByDefinitionName(Element target, string parameterName)
        {
            if (target == null || string.IsNullOrWhiteSpace(parameterName)) return null;

            foreach (Parameter parameter in target.Parameters)
            {
                var definitionName = parameter?.Definition?.Name;
                if (string.Equals(definitionName, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static bool TryWriteValue(Parameter parameter, string value, ApplyResult result, string scope, string target, string requestedName, string matchedName, string detail)
        {
            if (parameter == null) return false;
            if (parameter.IsReadOnly) return LogFailure(result, scope, target, requestedName, "read-only parameter; " + detail);

            try
            {
                if (!TrySetValue(parameter, value, out var reason))
                {
                    return LogFailure(result, scope, target, requestedName, "wrong storage type; " + reason + "; " + detail);
                }

                var aliasMatched = !string.Equals(requestedName?.Trim(), matchedName, StringComparison.Ordinal);
                LogSuccess(result, scope, target, requestedName, matchedName, aliasMatched, detail);
                return true;
            }
            catch (Exception ex)
            {
                return LogFailure(result, scope, target, requestedName,
                    "set failed; exception=" + ex.GetType().Name + ": " + ex.Message + "; " + detail);
            }
        }

        private static bool TrySetValue(Parameter parameter, string value, out string reason)
        {
            reason = string.Empty;
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.Set(value);
                case StorageType.Integer:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        return parameter.Set(intValue);
                    }

                    reason = "expected integer, got='" + value + "'";
                    return false;
                case StorageType.Double:
                    if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
                    {
                        return parameter.Set(doubleValue);
                    }

                    reason = "expected double, got='" + value + "'";
                    return false;
                case StorageType.ElementId:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
                    {
                        return parameter.Set(new ElementId(idValue));
                    }

                    reason = "expected element id integer, got='" + value + "'";
                    return false;
                default:
                    reason = "unsupported storage type: " + parameter.StorageType;
                    return false;
            }
        }

        private static void LogDiagnostic(ApplyResult result, string scope, string target, string parameter, string reason, Exception ex, string context)
        {
            var message = "Scope=" + scope + "; Target=" + target + "; Parameter=" + parameter + "; Status=Diagnostic; Reason=" + reason
                          + "; ExceptionType=" + ex.GetType().FullName
                          + "; Message=" + ex.Message
                          + "; Inner=" + (ex.InnerException?.GetType().FullName + ": " + ex.InnerException?.Message)
                          + "; Stack=" + ex.StackTrace
                          + "; Context=" + context;
            result?.Logs.Add(message);
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

        private static void LogSuccess(ApplyResult result, string scope, string target, string parameter, string matchedParameter, bool aliasMatched, string extraDetail)
        {
            var detail = string.IsNullOrWhiteSpace(matchedParameter)
                ? string.Empty
                : (aliasMatched ? "alias matched: " + matchedParameter : "matched: " + matchedParameter);

            if (!string.IsNullOrWhiteSpace(extraDetail))
            {
                detail = string.IsNullOrWhiteSpace(detail) ? extraDetail : detail + "; " + extraDetail;
            }

            result?.Logs.Add($"Scope={scope}; Target={target}; Parameter={parameter}; Status=Success; {detail}");
            result?.ReportRows.Add(new ParameterWriteReportRow { Scope = scope, Target = target, Parameter = parameter, Status = "Success", Reason = detail });
        }

        private class ParameterResolutionContext
        {
            public Parameter Parameter { get; set; }
            public string MatchName { get; set; }
            public string Detail { get; set; }
        }
    }
}
