using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace DfEIfcNamer.UI
{
    public partial class DfEPaneView : UserControl, IDockablePaneProvider
    {
        public DfEPaneView()
        {
            InitializeComponent();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        }
    }
}
