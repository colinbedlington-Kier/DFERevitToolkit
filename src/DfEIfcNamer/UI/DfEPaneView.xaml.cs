using System.ComponentModel;
using System.Windows;

namespace DfEIfcNamer.UI
{
    public partial class DfEPaneView : Window
    {
        private bool _allowClose;

        public DfEPaneView()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }
    }
}
