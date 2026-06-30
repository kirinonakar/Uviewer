using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Uviewer.Services
{
    internal sealed class ExternalProgramSettingsController
    {
        private readonly IntPtr _windowHandle;
        private readonly Func<XamlRoot> _getXamlRoot;
        private readonly Func<ElementTheme> _getRequestedTheme;
        private readonly Func<string> _getExternalProgramPath;
        private readonly Action<string> _setExternalProgramPath;
        private readonly Action<string> _setToolbarExternalProgramPath;
        private readonly Action _saveWindowSettings;
        private readonly Action<string> _showNotification;

        public ExternalProgramSettingsController(
            IntPtr windowHandle,
            Func<XamlRoot> getXamlRoot,
            Func<ElementTheme> getRequestedTheme,
            Func<string> getExternalProgramPath,
            Action<string> setExternalProgramPath,
            Action<string> setToolbarExternalProgramPath,
            Action saveWindowSettings,
            Action<string> showNotification)
        {
            _windowHandle = windowHandle;
            _getXamlRoot = getXamlRoot;
            _getRequestedTheme = getRequestedTheme;
            _getExternalProgramPath = getExternalProgramPath;
            _setExternalProgramPath = setExternalProgramPath;
            _setToolbarExternalProgramPath = setToolbarExternalProgramPath;
            _saveWindowSettings = saveWindowSettings;
            _showNotification = showNotification;
        }

        public async Task SelectExternalProgramAsync()
        {
            const double dialogContentWidth = 420;

            var input = new TextBox
            {
                Text = _getExternalProgramPath(),
                PlaceholderText = Strings.ExternalProgramPathPlaceholder,
                Width = dialogContentWidth,
                MaxWidth = dialogContentWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var browseButton = new Button
            {
                Content = Strings.ExternalProgramBrowseButton,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            browseButton.Click += async (_, _) =>
            {
                var file = await PickExternalProgramFileAsync();
                if (file == null) return;

                input.Text = file.Path;
                input.Focus(FocusState.Programmatic);
                input.Select(input.Text.Length, 0);
            };

            var validationText = new TextBlock
            {
                Text = Strings.ExternalProgramPathRequired,
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };
            input.TextChanged += (_, _) => validationText.Visibility = Visibility.Collapsed;

            var panel = new StackPanel
            {
                Width = dialogContentWidth,
                MaxWidth = dialogContentWidth,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.ExternalProgramPathDescription,
                        TextWrapping = TextWrapping.Wrap
                    },
                    input,
                    browseButton,
                    validationText
                }
            };

            var dialog = new ContentDialog
            {
                Title = Strings.ExternalProgramSettings,
                Content = panel,
                PrimaryButtonText = Strings.ExternalProgramSaveButton,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _getXamlRoot(),
                RequestedTheme = _getRequestedTheme()
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(ExplorerItemLaunchService.NormalizeExternalProgramPath(input.Text))) return;

                validationText.Visibility = Visibility.Visible;
                input.Focus(FocusState.Programmatic);
                args.Cancel = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var path = ExplorerItemLaunchService.NormalizeExternalProgramPath(input.Text);
            _setExternalProgramPath(path);
            _setToolbarExternalProgramPath(path);
            _saveWindowSettings();
            _showNotification(Strings.ExternalProgramConfiguredNotification(ExplorerItemLaunchService.GetExternalProgramDisplayName(path)));
        }

        private async Task<StorageFile?> PickExternalProgramFileAsync()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".cmd");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".com");

            return await picker.PickSingleFileAsync();
        }
    }
}
