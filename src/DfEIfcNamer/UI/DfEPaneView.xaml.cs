using System.ComponentModel;
using System.Windows.Controls;
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

        private void KierLogoImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (KierLogoImage != null)
            {
                KierLogoImage.Visibility = Visibility.Collapsed;
            }

            if (KierLogoFallbackText != null)
            {
                KierLogoFallbackText.Visibility = Visibility.Visible;
            }
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
