using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class ParameterService
    {
        private static readonly string[] TypeParameters =
        {
            "IfcName[Type]", "IfcDescription[Type]", "Classification", "Classification(2)", "Classification(3)",
            "Classification(4)", "Classification(5)", "Classification(6)", "Classification(7)", "Classification(8)", "Classification(9)"
        };

        private static readonly string[] InstanceParameters = { "IfcName", "IfcDescription" };
        private static readonly string[] ProjectInfoParameters = { "DfE_ProjectInfoJson", "DfE_NamingCounters" };

        public void BootstrapSharedParameters(Document doc)
        {
            using (var tg = new TransactionGroup(doc, "DfE IFC Bootstrap Parameters"))
            {
                tg.Start();
                EnsureSharedParameters(doc, TypeParameters, true);
                EnsureSharedParameters(doc, InstanceParameters, false);
                EnsureProjectInfoParameters(doc);
                tg.Assimilate();
            }
        }

        private static void EnsureSharedParameters(Document doc, IEnumerable<string> names, bool isType)
        {
            var app = doc.Application;
            var sharedPath = Path.Combine(Path.GetTempPath(), "DfEIfcNamer_SharedParams.txt");
            if (!File.Exists(sharedPath))
            {
                File.WriteAllText(sharedPath, "# DfE shared params\n");
            }

            app.SharedParametersFilename = sharedPath;
            var file = app.OpenSharedParameterFile();
            var group = file.Groups.get_Item("DfE IFC") ?? file.Groups.Create("DfE IFC");

            var set = app.Create.NewCategorySet();
            foreach (Category c in doc.Settings.Categories)
            {
                if (c.AllowsBoundParameters)
                {
                    set.Insert(c);
                }
            }

            using (var tx = new Transaction(doc, isType ? "Bind DfE Type Parameters" : "Bind DfE Instance Parameters"))
            {
                tx.Start();
                foreach (var name in names)
                {
                    var def = group.Definitions.get_Item(name) as ExternalDefinition;
                    if (def == null)
                    {
                        var opt = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text);
                        def = group.Definitions.Create(opt) as ExternalDefinition;
                    }

                    var binding = isType ? (Binding)app.Create.NewTypeBinding(set) : app.Create.NewInstanceBinding(set);
                    if (!doc.ParameterBindings.Contains(def))
                    {
                        doc.ParameterBindings.Insert(def, binding, GroupTypeId.Ifc);
                    }
                }
                tx.Commit();
            }
        }

        private static void EnsureProjectInfoParameters(Document doc)
        {
            var app = doc.Application;
            var file = app.OpenSharedParameterFile();
            var group = file.Groups.get_Item("DfE IFC") ?? file.Groups.Create("DfE IFC");

            var set = app.Create.NewCategorySet();
            set.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation));

            using (var tx = new Transaction(doc, "Bind DfE Project Parameters"))
            {
                tx.Start();
                foreach (var name in ProjectInfoParameters)
                {
                    var def = group.Definitions.get_Item(name) as ExternalDefinition;
                    if (def == null)
                    {
                        var opt = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text);
                        def = group.Definitions.Create(opt) as ExternalDefinition;
                    }

                    if (!doc.ParameterBindings.Contains(def))
                    {
                        doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(set), GroupTypeId.Data);
                    }
                }
                tx.Commit();
            }
        }
    }
}
