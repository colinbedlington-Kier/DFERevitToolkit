using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DfEIfcNamer.Services
{
    public class ParameterService
    {
        private const string SharedParameterGroupName = "DfE IFC Namer";

        public void BootstrapSharedParameters(Document doc)
        {
            EnsureIfcNameParameters(doc, null);
        }

        public void EnsureIfcNameParameters(Document doc, IList<Category> categories)
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
            var modelCategories = categories ?? doc.Settings.Categories.Cast<Category>()
                .Where(c => c != null && c.CategoryType == CategoryType.Model && !c.IsTagCategory && c.AllowsBoundParameters)
                .ToList();

            using (var tg = new TransactionGroup(doc, "DfE IFC Bootstrap Parameters"))
            {
                tg.Start();
                EnsureSharedParameter(doc, group, new[] { "IFCName", "IfcName" }, false, GroupTypeId.Ifc, modelCategories);
                EnsureSharedParameter(doc, group, new[] { "IFCName[Type]", "IfcName[Type]" }, true, GroupTypeId.Ifc, modelCategories);
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

        private static void EnsureSharedParameter(Document doc, DefinitionGroup group, IEnumerable<string> possibleNames, bool isType, ForgeTypeId groupTypeId, IList<Category> categories)
        {
            var app = doc.Application;
            var categorySet = app.Create.NewCategorySet();
            foreach (var category in categories)
            {
                categorySet.Insert(category);
            }

            using (var tx = new Transaction(doc, isType ? "Bind IFCName [Type]" : "Bind IFCName"))
            {
                tx.Start();
                ExternalDefinition definition = null;
                foreach (var name in possibleNames)
                {
                    definition = group.Definitions.get_Item(name) as ExternalDefinition;
                    if (definition != null)
                    {
                        break;
                    }
                }

                if (definition != null)
                {
                    var binding = isType ? (Binding)app.Create.NewTypeBinding(categorySet) : app.Create.NewInstanceBinding(categorySet);
                    if (!doc.ParameterBindings.Insert(definition, binding, groupTypeId))
                    {
                        doc.ParameterBindings.ReInsert(definition, binding, groupTypeId);
                    }
                }

                tx.Commit();
            }
        }
    }
}
