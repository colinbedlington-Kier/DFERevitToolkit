using System.ComponentModel;
using System.Windows;

namespace DfEIfcNamer.UI
{
    public partial class DfEFloatingWindow : Window
    {
        private bool _allowClose;

        public DfEFloatingWindow()
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
