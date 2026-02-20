using System;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Services;
using DfEIfcNamer.UI;
using DfEIfcNamer.ViewModels;

namespace DfEIfcNamer.App
{
    public class DfEApplication : IExternalApplication
    {
        private static DfEPaneView _paneView;
        private static readonly DockablePaneId PaneDockableId = new DockablePaneId(new Guid(AppSettings.DockablePaneId));

        public Result OnStartup(UIControlledApplication application)
        {
            var resourceService = new ResourceJsonService();
            var counterService = new CounterStateService();
            var parameterService = new ParameterService();
            var namingService = new NamingService(counterService);
            var auditService = new AuditService();

            var executionHandler = new RevitExecutionHandler(parameterService, namingService, resourceService, counterService, auditService);
            var externalEvent = ExternalEvent.Create(executionHandler);
            var requestDispatcher = new RevitRequestDispatcher(executionHandler, externalEvent);

            var viewModel = new MainViewModel(requestDispatcher, resourceService, counterService);
            _paneView = new DfEPaneView { DataContext = viewModel };

            application.RegisterDockablePane(PaneDockableId, AppSettings.DockablePaneTitle, _paneView);
            CreateRibbon(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
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
                // Panel already exists.
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
            var showPaneButton = new PushButtonData("DfEIfcNamer.ShowPane", AppSettings.RibbonButtonName, path, typeof(ShowPaneCommand).FullName);
            panel.AddItem(showPaneButton);

            var diagnosticsButton = new PushButtonData("DfEIfcNamer.Diagnostics", "DfEIfcNamer: Diagnostics", path, typeof(DiagnosticsCommand).FullName);
            panel.AddItem(diagnosticsButton);
        }

        public static DockablePaneId PaneId => PaneDockableId;
    }

    public class ShowPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var pane = commandData.Application.GetDockablePane(DfEApplication.PaneId);
            pane.Show();
            return Result.Succeeded;
        }
    }

    public class DiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var parameterService = new ParameterService();
            var resourceService = new ResourceJsonService();

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var sharedParamPath = parameterService.ResolveSharedParameterFilePath();
            var sharedExists = System.IO.File.Exists(sharedParamPath);
            var entitiesCount = resourceService.LoadEntityLibrary().Count;
            var classificationCount = resourceService.LoadClassificationSlots().Count;

            var body =
                "Assembly Path:\n" + assemblyPath + "\n\n" +
                "Shared Parameters Path:\n" + sharedParamPath + "\n" +
                "Shared Parameters File Exists: " + sharedExists + "\n\n" +
                "Embedded IFC2x3 Entities: " + entitiesCount + "\n" +
                "Classification Slots: " + classificationCount;

            TaskDialog.Show("DfE IFC Namer Diagnostics", body);
            return Result.Succeeded;
        }
    }
}
