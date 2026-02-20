using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DfEIfcNamer.Services
{
    public class ParameterService
    {
        private const string SharedParameterGroupName = "DfE IFC Namer";

        private static readonly string[] TypeParameters =
        {
            "IfcName[Type]", "IfcDescription[Type]", "Classification", "Classification(2)", "Classification(3)",
            "Classification(4)", "Classification(5)", "Classification(6)", "Classification(7)", "Classification(8)", "Classification(9)",
            "DfE_IFCPredefinedType", "DfE_UserDefinedPredefinedTypeValue", "DfE_IFCEntity"
        };

        private static readonly string[] InstanceParameters = { "IfcName", "IfcDescription", "DfE_IFCPredefinedType", "DfE_UserDefinedPredefinedTypeValue", "DfE_IFCEntity" };
        private static readonly string[] ProjectInfoParameters = { "DfE_ProjectInfoJson", "DfE_NamingCounters" };

        public void BootstrapSharedParameters(Document doc)
        {
            var sharedPath = ResolveSharedParameterFilePath();
            if (!EnsureSharedParameterFileConfigured(doc.Application, sharedPath))
            {
                return;
            }

            var file = doc.Application.OpenSharedParameterFile();
            if (file == null)
            {
                TaskDialog.Show("DfE IFC Namer", "Shared parameter file could not be opened. Expected file at:\n" + sharedPath);
                return;
            }

            var group = file.Groups.get_Item(SharedParameterGroupName) ?? file.Groups.Create(SharedParameterGroupName);

            using (var tg = new TransactionGroup(doc, "DfE IFC Bootstrap Parameters"))
            {
                tg.Start();
                EnsureSharedParameters(doc, group, TypeParameters, true, GroupTypeId.Ifc);
                EnsureSharedParameters(doc, group, InstanceParameters, false, GroupTypeId.Ifc);
                EnsureProjectInfoParameters(doc, group);
                tg.Assimilate();
            }
        }

        public string ResolveSharedParameterFilePath()
        {
            var addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            var resourcesPath = Path.Combine(addinFolder, "Resources", "DfE_IfcNamer_SharedParameters.txt");
            if (File.Exists(resourcesPath))
            {
                return resourcesPath;
            }

            return Path.Combine(addinFolder, "DfE_IfcNamer_SharedParameters.txt");
        }

        private static bool EnsureSharedParameterFileConfigured(Autodesk.Revit.ApplicationServices.Application app, string sharedPath)
        {
            if (!File.Exists(sharedPath))
            {
                TaskDialog.Show("DfE IFC Namer", "Shared parameter file not found. Expected file at:\n" + sharedPath);
                return false;
            }

            app.SharedParametersFilename = sharedPath;
            return true;
        }

        private static void EnsureSharedParameters(Document doc, DefinitionGroup group, IEnumerable<string> names, bool isType, ForgeTypeId groupTypeId)
        {
            var app = doc.Application;
            var categorySet = app.Create.NewCategorySet();
            foreach (Category category in doc.Settings.Categories)
            {
                try
                {
                    if (category != null && category.AllowsBoundParameters)
                    {
                        categorySet.Insert(category);
                    }
                }
                catch
                {
                    // Skip categories that cannot be bound.
                }
            }

            using (var tx = new Transaction(doc, isType ? "Bind DfE Type Parameters" : "Bind DfE Instance Parameters"))
            {
                tx.Start();
                foreach (var name in names)
                {
                    var definition = group.Definitions.get_Item(name) as ExternalDefinition;
                    if (definition == null)
                    {
                        continue;
                    }

                    var binding = isType ? (Binding)app.Create.NewTypeBinding(categorySet) : app.Create.NewInstanceBinding(categorySet);
                    if (!doc.ParameterBindings.Contains(definition))
                    {
                        var inserted = doc.ParameterBindings.Insert(definition, binding, groupTypeId);
                        if (!inserted)
                        {
                            System.Diagnostics.Debug.WriteLine("DfE IFC Namer: failed to bind parameter " + name);
                        }
                    }
                }

                tx.Commit();
            }
        }

        private static void EnsureProjectInfoParameters(Document doc, DefinitionGroup group)
        {
            var app = doc.Application;
            var set = app.Create.NewCategorySet();
            set.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation));

            using (var tx = new Transaction(doc, "Bind DfE Project Parameters"))
            {
                tx.Start();
                foreach (var name in ProjectInfoParameters)
                {
                    var definition = group.Definitions.get_Item(name) as ExternalDefinition;
                    if (definition == null)
                    {
                        continue;
                    }

                    if (!doc.ParameterBindings.Contains(definition))
                    {
                        var inserted = doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(set), GroupTypeId.Data);
                        if (!inserted)
                        {
                            System.Diagnostics.Debug.WriteLine("DfE IFC Namer: failed to bind project parameter " + name);
                        }
                    }
                }

                tx.Commit();
            }
        }
    }
}
