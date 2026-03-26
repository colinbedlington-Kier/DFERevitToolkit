using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public static class ParameterWriteAliases
    {
        public static readonly string[] IfcProjectName = { "IfcProjectName", "Project Name" };
        public static readonly string[] IfcProjectDescription = { "IfcDescription", "IfcProjectDescription", "Project Description" };
        public static readonly string[] IfcSiteName = { "IfcSiteName", "Site Name" };
        public static readonly string[] IfcSiteDescription = { "IfcSiteDescription", "Site Description" };
        public static readonly string[] IfcBuildingName = { "IfcBuildingName", "Building Name" };
        public static readonly string[] IfcBuildingDescription = { "IfcBuildingDescription", "Building Description" };
    }

    public abstract class ParameterWriterBase
    {
        protected static bool Set(Element element, string value, params string[] names)
        {
            foreach (var name in names)
            {
                var p = element?.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(value ?? string.Empty);
                    return true;
                }
            }
            return false;
        }
    }

    public class InstanceParameterWriter : ParameterWriterBase
    {
        public bool Write(Element element, string value, params string[] names) => Set(element, value, names);
    }

    public class TypeParameterWriter : ParameterWriterBase
    {
        public bool Write(Element typeElement, string value, params string[] names) => Set(typeElement, value, names);
    }

    public class ProjectParameterWriter : ParameterWriterBase
    {
        public bool Write(Document doc, string value, params string[] names) => Set(doc?.ProjectInformation, value, names);
    }
}
