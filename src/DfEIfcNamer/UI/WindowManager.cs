using Autodesk.Revit.UI;
using DfEIfcNamer.ViewModels;

namespace DfEIfcNamer.UI
{
    public static class WindowManager
    {
        private static DfEFloatingWindow _window;

        public static void ShowOrActivate(UIApplication uiApp, MainViewModel viewModel)
        {
            if (_window == null || !_window.IsLoaded)
            {
                _window = new DfEFloatingWindow
                {
                    DataContext = viewModel
                };
            }

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            viewModel.DocumentStatus = "Document: " + (uiApp.ActiveUIDocument?.Document?.Title ?? "n/a");
        }

        public static void CloseWindow()
        {
            _window?.ForceClose();
            _window = null;
        }
    }
}
