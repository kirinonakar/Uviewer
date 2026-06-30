using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Uviewer.Services
{
    internal sealed class FileOpenController
    {
        private readonly IntPtr _windowHandle;
        private readonly LocalDocumentOpenCoordinator _localDocumentOpenCoordinator;
        private readonly Action<string, string, string> _showNotification;

        public FileOpenController(
            IntPtr windowHandle,
            LocalDocumentOpenCoordinator localDocumentOpenCoordinator,
            Action<string, string, string> showNotification)
        {
            _windowHandle = windowHandle;
            _localDocumentOpenCoordinator = localDocumentOpenCoordinator ?? throw new ArgumentNullException(nameof(localDocumentOpenCoordinator));
            _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
        }

        public void HandleDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;

            if (e.DragUIOverride != null)
            {
                e.DragUIOverride.Caption = "이미지 열기";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        }

        public async void HandleDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();

                    if (items.Count > 0)
                    {
                        await _localDocumentOpenCoordinator.OpenDroppedStorageItemAsync(items[0]);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Grid_Drop: {ex.Message}");
                _showNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        public async Task OpenFileAsync()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };

            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

            _localDocumentOpenCoordinator.AddPickerFileTypeFilters(picker.FileTypeFilter);

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                await _localDocumentOpenCoordinator.OpenPickedFileAsync(file);
            }
        }

        public async Task OpenFolderAsync()
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };

            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();

            if (folder != null)
            {
                await _localDocumentOpenCoordinator.OpenPickedFolderAsync(folder);
            }
        }
    }
}
