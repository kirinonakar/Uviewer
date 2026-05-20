using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private readonly ImageViewerState _imageViewerState = new();
        private List<ImageEntry> _imageEntries
        {
            get => _imageViewerState.Entries;
            set => _imageViewerState.Entries = value ?? new List<ImageEntry>();
        }

        private int _currentIndex
        {
            get => _imageViewerState.CurrentIndex;
            set => _imageViewerState.CurrentIndex = value;
        }

        private double _zoomLevel { get => _zoomService.Level; set => _zoomService.SetLevel(value); }

        private CanvasBitmap? _currentBitmap
        {
            get => _imageViewerState.CurrentBitmap;
            set => _imageViewerState.CurrentBitmap = value;
        }

        private readonly ExplorerState _explorerState = new();
        private string? _currentExplorerPath
        {
            get => _explorerState.CurrentPath;
            set => _explorerState.CurrentPath = value;
        }

        private bool _isExplorerGrid
        {
            get => _explorerState.IsGridView;
            set => _explorerState.IsGridView = value;
        }

        private ExplorerSortMode _explorerSortMode
        {
            get => _explorerState.SortMode;
            set => _explorerState.SortMode = value;
        }

        private System.Collections.ObjectModel.ObservableCollection<FileItem> _fileItems => _explorerState.Items;

        private readonly BookmarkPanelState _bookmarkPanelState = new();
        private bool _isNavigatingRecent
        {
            get => _bookmarkPanelState.IsNavigatingRecent;
            set => _bookmarkPanelState.IsNavigatingRecent = value;
        }
        private System.Collections.ObjectModel.ObservableCollection<BookmarkViewModel> _fileFavoriteItems => _bookmarkPanelState.FileFavoriteItems;
        private System.Collections.ObjectModel.ObservableCollection<BookmarkViewModel> _folderFavoriteItems => _bookmarkPanelState.FolderFavoriteItems;
        private System.Collections.ObjectModel.ObservableCollection<BookmarkViewModel> _recentItemsList => _bookmarkPanelState.RecentItems;

        private FullscreenOverlayManager _overlayManager = null!;
        private WindowChromeController _windowChromeController = null!;
        private DispatcherQueueTimer? _notificationTimer;

        private bool _isSideBySideMode
        {
            get => _imageViewerState.IsSideBySideMode;
            set => _imageViewerState.IsSideBySideMode = value;
        }

        private bool _nextImageOnRight
        {
            get => _imageViewerState.NextImageOnRight;
            set => _imageViewerState.NextImageOnRight = value;
        }

        private bool _autoDoublePageForArchive
        {
            get => _imageViewerState.AutoDoublePageForArchive;
            set => _imageViewerState.AutoDoublePageForArchive = value;
        }

        private bool _isCurrentViewSideBySide
        {
            get => _imageViewerState.IsCurrentViewSideBySide;
            set => _imageViewerState.IsCurrentViewSideBySide = value;
        }

        private CanvasBitmap? _leftBitmap
        {
            get => _imageViewerState.LeftBitmap;
            set => _imageViewerState.LeftBitmap = value;
        }

        private CanvasBitmap? _rightBitmap
        {
            get => _imageViewerState.RightBitmap;
            set => _imageViewerState.RightBitmap = value;
        }

        private bool _matchControlDirection = false;
        private int _pendingPdfPageIndex = -1;

        private bool _sharpenEnabled;
        private bool _isAnimatedFrameActive;
        public ImageProcessingViewModel ImageOptions { get; } = new();

        private double _pdfPanY = 0;
        private double _pdfPanX = 0;
        private double _lastCanvasWidth = 0;
        private volatile bool _isPdfTransitioning = false;
        private int _pdfScrollDirection = 1;
        private bool _isSeamlessScroll = false;
        private bool _allowMultipleInstances = true;
        private bool _isRegistered = false;
        private double _explorerThumbnailSize = 80;
        private bool _showFolderThumbnails = false;

        private bool ShouldInvertControls
        {
            get
            {
                if (_currentPdfDocument != null) return false;
                if (!_matchControlDirection || _nextImageOnRight) return false;
                if (_isTextMode) return false;

                if (_isEpubMode)
                {
                    if (EpubSelectedItem is Grid g && !(g.Tag is EpubImageTag)) return false;

                    if (_epubChapterHasText.TryGetValue(_currentEpubChapterIndex, out var curHasText) && curHasText) return false;

                    bool prevHasText = _currentEpubChapterIndex > 0 &&
                                      _epubChapterHasText.TryGetValue(_currentEpubChapterIndex - 1, out var pHT) && pHT;
                    bool nextHasText = _currentEpubChapterIndex < _epubSpine.Count - 1 &&
                                      _epubChapterHasText.TryGetValue(_currentEpubChapterIndex + 1, out var nHT) && nHT;

                    if (prevHasText && nextHasText) return false;
                }

                return true;
            }
        }
    }
}
