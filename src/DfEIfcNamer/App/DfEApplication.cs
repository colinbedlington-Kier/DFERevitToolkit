using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Services;
using DfEIfcNamer.UI;
using DfEIfcNamer.ViewModels;

namespace DfEIfcNamer.App
{
    public class DfEApplication : IExternalApplication
    {
        private static DfEPaneView? _paneView;
        private static DockablePaneId _paneId = new DockablePaneId(new Guid(AppSettings.DockablePaneId));

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

            application.RegisterDockablePane(_paneId, AppSettings.DockablePaneTitle, _paneView);
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
                // panel already exists
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
            var data = new PushButtonData("DfEIfcNamer.ShowPane", AppSettings.RibbonButtonName, path, typeof(ShowPaneCommand).FullName);
            panel.AddItem(data);
        }

        public static DockablePaneId PaneId => _paneId;
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
}
