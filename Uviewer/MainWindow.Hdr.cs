using System;
using System.IO;
using System.Threading.Tasks;
using AppDisplayInformation = Microsoft.Graphics.Display.DisplayInformation;
using AppDisplayAdvancedColorKind = Microsoft.Graphics.Display.DisplayAdvancedColorKind;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private AppDisplayInformation? _hdrDisplayInformation;
        private bool _isHdrOutputActive;

        private void InitializeHdrDisplayState()
        {
            if (_hdrDisplayInformation != null) return;

            try
            {
                _hdrDisplayInformation = AppDisplayInformation.CreateForWindowId(AppWindow.Id);
                _hdrDisplayInformation.AdvancedColorInfoChanged += HdrDisplayInformation_AdvancedColorInfoChanged;
                _isHdrOutputActive = QueryHdrOutputActive();
            }
            catch (Exception ex)
            {
                // Safe default: never send extended-range pixels to an SDR output.
                _isHdrOutputActive = false;
                System.Diagnostics.Debug.WriteLine($"HDR display detection unavailable; using SDR: {ex.Message}");
            }
        }

        private void HdrDisplayInformation_AdvancedColorInfoChanged(
            AppDisplayInformation sender,
            object args)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ApplyHdrDisplayStateChangeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HDR display change handling failed: {ex.Message}");
                }
            });
        }

        private bool QueryHdrOutputActive()
        {
            try
            {
                return _hdrDisplayInformation?.GetAdvancedColorInfo().CurrentAdvancedColorKind ==
                    AppDisplayAdvancedColorKind.HighDynamicRange;
            }
            catch
            {
                return false;
            }
        }

        private async Task ApplyHdrDisplayStateChangeAsync()
        {
            bool isActive = QueryHdrOutputActive();
            if (isActive == _isHdrOutputActive) return;

            _isHdrOutputActive = isActive;
            _hdrSwapChainRenderer.Hide(HdrMainCanvas);
            _hdrSwapChainRenderer.Hide(HdrLeftCanvas);
            _hdrSwapChainRenderer.Hide(HdrRightCanvas);

            if (_imageViewerController == null || !IsCurrentEntryAvif())
            {
                MainCanvas?.Invalidate();
                LeftCanvas?.Invalidate();
                RightCanvas?.Invalidate();
                return;
            }

            // The cached bitmap format depends on display mode: FP16 scRGB while
            // HDR is active, and the Windows codec's SDR bitmap otherwise.
            _imageViewerController.PrepareForImageLoad();
            await _imageViewerController.DisplayCurrentImageAsync();
        }

        private bool IsCurrentEntryAvif()
        {
            if (_imageViewerState.CurrentIndex < 0 ||
                _imageViewerState.CurrentIndex >= _imageViewerState.Entries.Count)
                return false;

            var entry = _imageViewerState.Entries[_imageViewerState.CurrentIndex];
            string? sourceName = entry.ArchiveEntryKey ?? entry.WebDavPath ?? entry.FilePath;
            return string.Equals(Path.GetExtension(sourceName), ".avif", StringComparison.OrdinalIgnoreCase);
        }

        private void DisposeHdrDisplayState()
        {
            if (_hdrDisplayInformation == null) return;

            try
            {
                _hdrDisplayInformation.AdvancedColorInfoChanged -= HdrDisplayInformation_AdvancedColorInfoChanged;
                _hdrDisplayInformation.Dispose();
            }
            catch { }
            finally
            {
                _hdrDisplayInformation = null;
            }
        }
    }
}
