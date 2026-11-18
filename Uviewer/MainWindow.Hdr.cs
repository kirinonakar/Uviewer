using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;
using AppDisplayInformation = Microsoft.Graphics.Display.DisplayInformation;
using AppDisplayAdvancedColorKind = Microsoft.Graphics.Display.DisplayAdvancedColorKind;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private AppDisplayInformation? _hdrDisplayInformation;
        private bool _isHdrOutputActive;
        private float _hdrDisplayMaxLuminance = 1000.0f;

        private void InitializeHdrDisplayState()
        {
            if (_hdrDisplayInformation != null) return;

            try
            {
                _hdrDisplayInformation = AppDisplayInformation.CreateForWindowId(AppWindow.Id);
                _hdrDisplayInformation.AdvancedColorInfoChanged += HdrDisplayInformation_AdvancedColorInfoChanged;
                var state = QueryHdrOutputState();
                _isHdrOutputActive = state.IsActive;
                _hdrDisplayMaxLuminance = state.MaxLuminance;
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

        private (bool IsActive, float MaxLuminance) QueryHdrOutputState()
        {
            try
            {
                var colorInfo = _hdrDisplayInformation?.GetAdvancedColorInfo();
                if (colorInfo == null) return (false, _hdrDisplayMaxLuminance);

                float maxLuminance = _hdrDisplayMaxLuminance;
                if (double.IsFinite(colorInfo.MaxLuminanceInNits) && colorInfo.MaxLuminanceInNits > 80)
                {
                    maxLuminance = (float)Math.Clamp(
                        colorInfo.MaxLuminanceInNits,
                        80.0,
                        10000.0);
                }

                bool isActive = colorInfo.CurrentAdvancedColorKind ==
                    AppDisplayAdvancedColorKind.HighDynamicRange;
                return (isActive, maxLuminance);
            }
            catch
            {
                return (false, _hdrDisplayMaxLuminance);
            }
        }

        private async Task ApplyHdrDisplayStateChangeAsync()
        {
            var state = QueryHdrOutputState();
            bool modeChanged = state.IsActive != _isHdrOutputActive;
            bool peakChanged = state.IsActive &&
                Math.Abs(state.MaxLuminance - _hdrDisplayMaxLuminance) > 1.0f;
            if (!modeChanged && !peakChanged) return;

            _isHdrOutputActive = state.IsActive;
            _hdrDisplayMaxLuminance = state.MaxLuminance;
            _imageSwapChainRenderer.Hide(HdrMainCanvas);
            _imageSwapChainRenderer.Hide(HdrLeftCanvas);
            _imageSwapChainRenderer.Hide(HdrRightCanvas);

            bool hasHdrEntries = _imageViewerController != null && _imageViewerState.Entries.Any(UsesHdrDecode);
            if (hasHdrEntries)
            {
                // SDR/HDR mode and peak luminance are part of the decoded bitmap's
                // state. Invalidate preloaded and sharpened copies together.
                _preloadManager.CancelAll();
                _imageCache.ClearAllExcept(_currentBitmap, _leftBitmap, _rightBitmap);
            }

            if (_imageViewerController == null || !hasHdrEntries ||
                (!IsDisplayingHdrFormat() && !_imageViewerController.IsImageDisplayPending))
            {
                MainCanvas?.Invalidate();
                LeftCanvas?.Invalidate();
                RightCanvas?.Invalidate();
                return;
            }

            _imageViewerController.PrepareForImageLoad();
            await _imageViewerController.DisplayCurrentImageAsync();
        }

        private bool IsDisplayingHdrFormat()
        {
            var entries = _imageViewerState.Entries;
            int index = _imageViewerState.CurrentIndex;
            if (index < 0 || index >= entries.Count)
                return false;

            return UsesHdrDecode(entries[index]) ||
                   (_isCurrentViewSideBySide &&
                    UsesHdrDecode(entries[FileExplorerService.GetNextImageIndex(entries, index, 1, true)]));
        }

        private static bool UsesHdrDecode(ImageEntry entry)
        {
            string? sourceName = entry.ArchiveEntryKey ?? entry.WebDavPath ?? entry.FilePath;
            return JxlImageDecoder.IsJxl(sourceName) ||
                   string.Equals(Path.GetExtension(sourceName), ".avif", StringComparison.OrdinalIgnoreCase);
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
