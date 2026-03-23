using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class ParameterService
    {
        private const string SharedParameterGroupName = "DfE IFC Namer";
        private const string SharedParameterFileName = "DfE_IfcNamer_SharedParameters.txt";

        private static readonly string[] InstanceParameterNames =
        {
            "IFCName",
            "IfcDescription"
        };

        private static readonly string[] TypeParameterNames =
        {
            "IFCName [Type]",
            "IFCName[Type]",
            "IfcName[Type]",
            "IfcDescription[Type]",
            "Classification",
            "Classification(2)",
            "Classification(3)",
            "Classification(4)",
            "Classification(5)",
            "Classification(6)",
            "Classification(7)",
            "Classification(8)",
            "Classification(9)",
            "DfE_IFCPredefinedType",
            "DfE_UserDefinedPredefinedTypeValue",
            "DfE_IFCEntity"
        };

        private static readonly string[] ProjectInfoParameterNames =
        {
            "DfE_ProjectInfoJson",
            "DfE_NamingCounters"
        };

        public string ResolveAddinFolder()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        }

        public string ResolveSharedParameterFilePath()
        {
            var addinFolder = ResolveAddinFolder();
            var resourcesPath = Path.Combine(addinFolder, "Resources", SharedParameterFileName);
            if (File.Exists(resourcesPath))
            {
                return resourcesPath;
            }

            return Path.Combine(addinFolder, SharedParameterFileName);
        }

        public ParameterBindingSummary EnsureIfcNameParameters(Document doc, IList<Category> categories)
        {
            var summary = new ParameterBindingSummary();
            var sharedPath = ResolveSharedParameterFilePath();
            summary.SharedParameterFilePath = sharedPath;

            if (!EnsureSharedParameterFileConfigured(doc.Application, sharedPath))
            {
                summary.ErrorMessage = "Shared parameter file missing at: " + sharedPath;
                return summary;
            }

            var file = doc.Application.OpenSharedParameterFile();
            if (file == null)
            {
                summary.ErrorMessage = "OpenSharedParameterFile() returned null. Expected: " + sharedPath;
                return summary;
            }

            var group = file.Groups.get_Item(SharedParameterGroupName);
            if (group == null)
            {
                summary.ErrorMessage = "Shared parameter group not found: " + SharedParameterGroupName;
                return summary;
            }

            var allModelCategories = doc.Settings.Categories.Cast<Category>().ToList();
            var selectedIds = categories == null
                ? null
                : new HashSet<long>(categories.Where(c => c != null).Select(c => c.Id.Value));
            var targetCategories = BuildValidCategoryList(allModelCategories, selectedIds, summary);

            var projectInfoCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);
            if (projectInfoCategory != null)
            {
                targetCategories.ProjectInfoCategories.Add(projectInfoCategory);
            }

            using (var tg = new TransactionGroup(doc, "DfE IFC Bootstrap Parameters"))
            {
                tg.Start();
                EnsureParameterSet(doc, group, InstanceParameterNames, isType: false, GroupTypeId.Ifc, targetCategories.ValidModelCategories, summary);
                EnsureParameterSet(doc, group, TypeParameterNames, isType: true, GroupTypeId.Ifc, targetCategories.ValidModelCategories, summary);
                EnsureParameterSet(doc, group, ProjectInfoParameterNames, isType: false, GroupTypeId.Data, targetCategories.ProjectInfoCategories, summary);
                tg.Assimilate();
            }

            return summary;
        }

        private static ValidCategorySelection BuildValidCategoryList(IEnumerable<Category> inputCategories, HashSet<long> selectedIds, ParameterBindingSummary summary)
        {
            var result = new ValidCategorySelection();
            foreach (var category in inputCategories.Where(c => c != null).OrderBy(c => c.Name))
            {
                if (selectedIds != null && selectedIds.Count > 0 && !selectedIds.Contains(category.Id.Value))
                {
                    continue;
                }

                if (!category.AllowsBoundParameters || category.IsTagCategory || category.CategoryType == CategoryType.Internal)
                {
                    summary.SkippedUnsupportedCategoriesCount++;
                    continue;
                }

                try
                {
                    result.ValidModelCategories.Add(category);
                    summary.IncludedCategoryNames.Add(category.Name);
                    summary.IncludedCategoriesCount++;
                }
                catch
                {
                    summary.SkippedUnsupportedCategoriesCount++;
                }
            }

            return result;
        }

        private static bool EnsureSharedParameterFileConfigured(Autodesk.Revit.ApplicationServices.Application app, string sharedPath)
        {
            if (!File.Exists(sharedPath))
            {
                return false;
            }

            app.SharedParametersFilename = sharedPath;
            return true;
        }

        private static void EnsureParameterSet(Document doc, DefinitionGroup group, IEnumerable<string> parameterNames, bool isType, ForgeTypeId groupTypeId, IList<Category> categories, ParameterBindingSummary summary)
        {
            foreach (var parameterName in parameterNames)
            {
                EnsureSharedParameter(doc, group, parameterName, isType, groupTypeId, categories, summary);
            }
        }

        private static void EnsureSharedParameter(Document doc, DefinitionGroup group, string parameterName, bool isType, ForgeTypeId groupTypeId, IList<Category> categories, ParameterBindingSummary summary)
        {
            var app = doc.Application;
            var categorySet = app.Create.NewCategorySet();
            foreach (var category in categories)
            {
                try
                {
                    categorySet.Insert(category);
                }
                catch
                {
                    summary.SkippedUnsupportedCategoriesCount++;
                }
            }

            using (var tx = new Transaction(doc, "Bind " + parameterName))
            {
                tx.Start();

                var definition = group.Definitions.get_Item(parameterName) as ExternalDefinition;
                if (definition == null)
                {
                    tx.RollBack();
                    summary.FailedBindingInsertCount++;
                    return;
                }

                var binding = isType ? (Binding)app.Create.NewTypeBinding(categorySet) : app.Create.NewInstanceBinding(categorySet);
                if (!doc.ParameterBindings.Insert(definition, binding, groupTypeId) && !doc.ParameterBindings.ReInsert(definition, binding, groupTypeId))
                {
                    summary.FailedBindingInsertCount++;
                }

                tx.Commit();
            }
        }

        public class ParameterBindingSummary
        {
            public string SharedParameterFilePath { get; set; }
            public int IncludedCategoriesCount { get; set; }
            public int SkippedUnsupportedCategoriesCount { get; set; }
            public int FailedBindingInsertCount { get; set; }
            public string ErrorMessage { get; set; }
            public IList<string> IncludedCategoryNames { get; } = new List<string>();
        }

        private class ValidCategorySelection
        {
            public IList<Category> ValidModelCategories { get; } = new List<Category>();
            public IList<Category> ProjectInfoCategories { get; } = new List<Category>();
        }
    }
}
