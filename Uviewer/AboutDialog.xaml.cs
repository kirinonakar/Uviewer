using Microsoft.UI.Xaml.Controls;
using System;
using System.Reflection;
using Windows.ApplicationModel;

namespace Uviewer
{
    public sealed partial class AboutDialog : ContentDialog
    {
        public string VersionText { get; }

        public AboutDialog()
        {
            this.InitializeComponent();
            
            VersionText = $"Uviewer ({GetVersionString()})";
        }

        private static string GetVersionString()
        {
            try
            {
                var version = Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                var version = typeof(AboutDialog).Assembly.GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}" : "0.0.0.0";
            }
        }

        private void CloseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
