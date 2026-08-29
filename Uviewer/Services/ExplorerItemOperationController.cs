using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading.Tasks;
using Uviewer.Models;
using Windows.System;

namespace Uviewer.Services
{
    internal sealed class ExplorerItemOperationHandlers
    {
        public Func<XamlRoot> GetXamlRoot { get; init; } = null!;
        public Func<ElementTheme> GetRequestedTheme { get; init; } = null!;
        public Func<string> GetExternalProgramPath { get; init; } = null!;
        public Func<Task> SelectExternalProgramAsync { get; init; } = null!;
        public Action RefreshExplorer { get; init; } = null!;
        public Func<string, bool, bool> IsTargetOpen { get; init; } = null!;
        public Func<string, bool, Task> ReleaseCurrentDocumentAsync { get; init; } = null!;
        public Func<string, Task> OpenLocalFilePathAsync { get; init; } = null!;
        public Func<string, string?> GetNextImagePathAfterDelete { get; init; } = null!;
        public Action ClearViewer { get; init; } = null!;
        public Action<string, string, string> ShowNotification { get; init; } = null!;
    }

    internal sealed class ExplorerItemOperationController
    {
        private readonly ExplorerItemLaunchService _launchService;
        private readonly ExplorerItemOperationHandlers _handlers;
        private ContentDialog? _activeDeleteDialog;
        private Button? _deleteButton;
        private Button? _cancelButton;

        public bool IsDeleteDialogOpen => _activeDeleteDialog != null;

        public ExplorerItemOperationController(
            ExplorerItemLaunchService launchService,
            ExplorerItemOperationHandlers handlers)
        {
            _launchService = launchService;
            _handlers = handlers;
        }

        public async Task OpenWithExternalProgramAsync(FileItem? item)
        {
            await _launchService.OpenWithExternalProgramAsync(
                item,
                _handlers.GetExternalProgramPath(),
                _handlers.SelectExternalProgramAsync,
                _handlers.RefreshExplorer,
                _handlers.ShowNotification);
        }

        public void OpenWithDefaultProgram(FileItem? item)
        {
            _launchService.OpenWithDefaultProgram(
                item,
                _handlers.RefreshExplorer,
                _handlers.ShowNotification);
        }

        public void OpenInWindowsExplorer(FileItem? item)
        {
            _launchService.OpenInWindowsExplorer(
                item,
                _handlers.RefreshExplorer,
                _handlers.ShowNotification);
        }

        public async Task RenameAsync(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var originalPath = item.FullPath;
            if (!File.Exists(originalPath) && !Directory.Exists(originalPath))
            {
                _handlers.ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                _handlers.RefreshExplorer();
                return;
            }

            var input = new TextBox
            {
                Text = item.Name,
                SelectionStart = 0,
                SelectionLength = Path.GetFileNameWithoutExtension(item.Name).Length,
                MinWidth = 320
            };

            var dialog = new ContentDialog
            {
                Title = Strings.ExplorerRename,
                Content = input,
                PrimaryButtonText = Strings.RenamePrimary,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _handlers.GetXamlRoot(),
                RequestedTheme = _handlers.GetRequestedTheme()
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var newName = input.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == item.Name) return;

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _handlers.ShowNotification(Strings.InvalidFileName, "\uE783", "Red");
                return;
            }

            var parent = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(parent)) return;

            var newPath = Path.Combine(parent, newName);
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                _handlers.ShowNotification(Strings.FileNameAlreadyExists, "\uE783", "Red");
                return;
            }

            var shouldReopen = _handlers.IsTargetOpen(originalPath, item.IsDirectory);
            await _handlers.ReleaseCurrentDocumentAsync(originalPath, item.IsDirectory);

            try
            {
                if (item.IsDirectory)
                {
                    Directory.Move(originalPath, newPath);
                }
                else
                {
                    File.Move(originalPath, newPath);
                }

                _handlers.RefreshExplorer();

                if (shouldReopen && !item.IsDirectory)
                {
                    await _handlers.OpenLocalFilePathAsync(newPath);
                }

                _handlers.ShowNotification(Strings.RenameSucceeded, "\uE735", "Gold");
            }
            catch (Exception ex)
            {
                _handlers.ShowNotification(Strings.RenameFailed(ex.Message), "\uE783", "Red");
            }
        }

        public async Task DeleteAsync(FileItem? item)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var path = item.FullPath;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _handlers.ShowNotification(Strings.FileNotFound, "\uE7BA", "Red");
                _handlers.RefreshExplorer();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Strings.ExplorerDelete,
                Content = Strings.DeleteConfirmation(item.Name),
                PrimaryButtonText = Strings.DeletePrimary,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _handlers.GetXamlRoot(),
                RequestedTheme = _handlers.GetRequestedTheme()
            };

            dialog.Opened += (_, _) =>
            {
                _deleteButton = FindButtonByText(dialog, dialog.PrimaryButtonText);
                _cancelButton = FindButtonByText(dialog, dialog.CloseButtonText);
                _cancelButton?.Focus(FocusState.Programmatic);
            };

            dialog.PreviewKeyDown += (_, e) =>
            {
                e.Handled = HandleDeleteDialogKey(e.Key);
            };

            ContentDialogResult result;
            _activeDeleteDialog = dialog;
            try
            {
                result = await dialog.ShowAsync();
            }
            finally
            {
                _activeDeleteDialog = null;
                _deleteButton = null;
                _cancelButton = null;
            }

            if (result != ContentDialogResult.Primary) return;

            var shouldClearViewer = _handlers.IsTargetOpen(path, item.IsDirectory);
            var nextImagePath = shouldClearViewer && !item.IsDirectory
                ? _handlers.GetNextImagePathAfterDelete(path)
                : null;
            await _handlers.ReleaseCurrentDocumentAsync(path, item.IsDirectory);

            try
            {
                if (item.IsDirectory)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                _handlers.RefreshExplorer();
                if (shouldClearViewer)
                {
                    if (string.IsNullOrEmpty(nextImagePath))
                    {
                        _handlers.ClearViewer();
                    }
                    else
                    {
                        await _handlers.OpenLocalFilePathAsync(nextImagePath);
                    }
                }

                _handlers.ShowNotification(Strings.MovedToRecycleBin, "\uE735", "Gold");
            }
            catch (Exception ex)
            {
                _handlers.ShowNotification(Strings.DeleteFailed(ex.Message), "\uE783", "Red");
            }
        }

        public bool HandleDeleteDialogKey(VirtualKey key)
        {
            if (_activeDeleteDialog == null)
            {
                return false;
            }

            if (key == VirtualKey.Left)
            {
                _deleteButton?.Focus(FocusState.Keyboard);
                return true;
            }

            if (key == VirtualKey.Right)
            {
                _cancelButton?.Focus(FocusState.Keyboard);
                return true;
            }

            if (key == VirtualKey.Escape)
            {
                _activeDeleteDialog.Hide();
                return true;
            }

            return false;
        }

        private static Button? FindButtonByText(DependencyObject parent, string text)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is Button button &&
                    ((button.Content is string content && content == text) ||
                     (button.Content is TextBlock textBlock && textBlock.Text == text)))
                {
                    return button;
                }

                var descendant = FindButtonByText(child, text);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
