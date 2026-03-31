using Microsoft.UI.Xaml.Controls;
using System;
using System.Reflection;

namespace Uviewer
{
    public sealed partial class AboutDialog : ContentDialog
    {
        public string VersionText { get; }

        public AboutDialog()
        {
            this.InitializeComponent();
            
            var assembly = typeof(AboutDialog).Assembly;
            var version = assembly.GetName().Version;
            var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}" : "0.0.0.0";
            VersionText = $"Uviewer ({versionString})";
        }

        private void CloseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
