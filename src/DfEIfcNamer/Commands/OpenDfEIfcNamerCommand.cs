using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.App;

namespace DfEIfcNamer.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenDfEIfcNamerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DfEApplication.ShowMainWindow(commandData.Application);
            return Result.Succeeded;
        }
    }
}
