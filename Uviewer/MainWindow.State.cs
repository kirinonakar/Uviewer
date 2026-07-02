using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading;
using Uviewer.Models;
using Uviewer.Services;
using Windows.Data.Pdf;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private readonly ImageViewerState _imageViewerState = new();
        private EpubReaderController _epubReaderController = null!;
        private ImageExifDialogService _imageExifDialogService = null!;

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
        private MainToolbarController _mainToolbarController = null!;
        private MainWindowControlEventBinder _controlEventBinder = null!;
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

        private PdfDocument? _currentPdfDocument => _pdfDocumentController?.CurrentDocument;
        private string? _currentPdfPath => _pdfDocumentController?.CurrentPath;

        private EpubSession _epubSession => _epubReaderController.Session;
        private EpubDocumentService _epubDocumentService => _epubReaderController.DocumentService;
        private EpubPageFlowService _epubPageFlowService => _epubReaderController.PageFlowService;
        private IReadOnlyList<string> _epubSpine => _epubReaderController?.Spine ?? Array.Empty<string>();
        private Dictionary<int, List<EpubWin2DPage>> _epubPreloadCache => _epubReaderController.PreloadCache;
        private Dictionary<int, bool> _epubChapterHasText => _epubReaderController.ChapterHasText;

        private bool _isEpubMode
        {
            get => _epubReaderController?.IsEpubMode == true;
            set => _epubReaderController.IsEpubMode = value;
        }

        private int _currentEpubChapterIndex
        {
            get => _epubReaderController.CurrentChapterIndex;
            set => _epubReaderController.CurrentChapterIndex = value;
        }

        private int _currentEpubPageIndex
        {
            get => _epubReaderController.CurrentPageIndex;
            set => _epubReaderController.CurrentPageIndex = value;
        }

        private List<EpubWin2DPage> _epubWin2DPages
        {
            get => _epubReaderController.Pages;
            set => _epubReaderController.Pages = value;
        }

        private int _pendingEpubStartBlockIndex
        {
            get => _epubReaderController.PendingStartBlockIndex;
            set => _epubReaderController.PendingStartBlockIndex = value;
        }

        public int PendingEpubChapterIndex
        {
            get => _epubReaderController.PendingChapterIndex;
            set => _epubReaderController.PendingChapterIndex = value;
        }

        public int PendingEpubPageIndex
        {
            get => _epubReaderController.PendingPageIndex;
            set => _epubReaderController.PendingPageIndex = value;
        }

        public int CurrentEpubChapterIndex => _epubReaderController.CurrentChapterIndex;
        public int CurrentEpubPageIndex => _epubReaderController.CurrentPageIndex;

        private string? _currentEpubFilePath => _epubReaderController.CurrentFilePath;
        private string? _currentEpubDisplayName => _epubReaderController.CurrentDisplayName;
        private EpubWin2DPage? CurrentEpubWin2DPage => _epubReaderController.CurrentPage;
        private object? EpubSelectedItem => _epubReaderController.SelectedItem;
        private List<UIElement> _epubPages => _epubReaderController.PageElements;

        private bool _sharpenEnabled
        {
            get => _imageViewerState.IsSharpenEnabled;
            set => _imageViewerState.IsSharpenEnabled = value;
        }

        private bool _isAnimatedFrameActive
        {
            get => _imageViewerState.IsAnimatedFrameActive;
            set => _imageViewerState.IsAnimatedFrameActive = value;
        }

        public ImageProcessingViewModel ImageOptions { get; } = new();

        private double _lastCanvasWidth
        {
            get => _imageViewerState.LastCanvasWidth;
            set => _imageViewerState.LastCanvasWidth = value;
        }

        private bool _isSeamlessScroll
        {
            get => _imageViewerState.IsSeamlessScroll;
            set => _imageViewerState.IsSeamlessScroll = value;
        }

        private CancellationTokenSource? _imageLoadingCts
        {
            get => _imageViewerState.ImageLoadingCts;
            set => _imageViewerState.ImageLoadingCts = value;
        }

        private bool _allowMultipleInstances = true;
        private bool _isRegistered = false;
        private double _explorerThumbnailSize = 80;
        private bool _showFolderThumbnails = false;
        private string _externalProgramPath = AppSettings.DefaultExternalProgramPath;
        private FileItem? _explorerContextItem;

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
