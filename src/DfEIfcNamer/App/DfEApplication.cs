using System;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.Commands;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Services;
using DfEIfcNamer.UI;
using DfEIfcNamer.ViewModels;

namespace DfEIfcNamer.App
{
    public class DfEApplication : IExternalApplication
    {
        private static MainViewModel _viewModel;

        public Result OnStartup(UIControlledApplication application)
        {
            var parameterService = new ParameterService();
            var cobieSyncService = new CobieSyncService(parameterService);
            var settingsStore = new ProjectSettingsStore();

            var executionHandler = new RevitExecutionHandler(cobieSyncService, settingsStore);
            var externalEvent = ExternalEvent.Create(executionHandler);
            var requestDispatcher = new RevitRequestDispatcher(executionHandler, externalEvent);

            _viewModel = new MainViewModel(requestDispatcher);
            _viewModel.RequestClose += () => WindowManager.CloseWindow();

            CreateRibbon(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            WindowManager.CloseWindow();
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication app)
        {
            try
            {
                app.CreateRibbonPanel(AppSettings.RibbonPanelName);
            }
            catch
            {
            }

            RibbonPanel panel = null;
            foreach (var item in app.GetRibbonPanels())
            {
                if (item.Name == AppSettings.RibbonPanelName)
                {
                    panel = item;
                    break;
                }
            }

            if (panel == null)
            {
                return;
            }

            var path = Assembly.GetExecutingAssembly().Location;
            var showWindowButton = new PushButtonData("DfEIfcNamer.OpenWindow", AppSettings.RibbonButtonName, path, typeof(OpenDfEIfcNamerCommand).FullName);
            panel.AddItem(showWindowButton);
        }

        public static void ShowMainWindow(UIApplication uiApp)
        {
            WindowManager.ShowOrActivate(uiApp, _viewModel);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var parameterService = new ParameterService();
            var sharedParamPath = parameterService.ResolveSharedParameterFilePath();
            var sharedExists = System.IO.File.Exists(sharedParamPath);
            TaskDialog.Show("DfE IFC Namer Diagnostics", "Shared Parameters File Exists: " + sharedExists + "\n" + sharedParamPath);
            return Result.Succeeded;
        }
    }
}
