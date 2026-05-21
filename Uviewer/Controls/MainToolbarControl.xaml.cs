using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer.Controls
{
    public sealed partial class MainToolbarControl : UserControl
    {
        public event EventHandler? ChangeFontRequested;
        public event EventHandler<string>? EncodingSelected;
        public event EventHandler? ChangeColorsRequested;
        public event EventHandler? ChangeUiFontRequested;
        public event EventHandler<string>? LanguageSelected;
        public event EventHandler<bool>? MatchControlDirectionChanged;
        public event EventHandler<bool>? AllowMultipleInstancesChanged;
        public event EventHandler<bool>? AutoDoublePageForArchiveChanged;
        public event EventHandler? AboutRequested;
        public event EventHandler? GlobalThemeToggleRequested;
        public event EventHandler? PinToggleRequested;
        public event EventHandler? AlwaysOnTopToggleRequested;
        public event EventHandler? ToggleSidebarRequested;
        public event EventHandler? AddToFavoritesRequested;
        public event EventHandler? OpenFileRequested;
        public event EventHandler? OpenFolderRequested;
        public event EventHandler? PdfTocRequested;
        public event ItemClickEventHandler? PdfTocItemClicked;
        public event EventHandler? PdfGoToPageRequested;
        public event RightTappedEventHandler? SearchRequested;
        public event EventHandler<double>? ZoomLevelChanged;
        public event EventHandler? ZoomOutRequested;
        public event EventHandler? ZoomInRequested;
        public event EventHandler? ZoomFitRequested;
        public event EventHandler? ZoomActualRequested;
        public event EventHandler? AozoraToggleRequested;
        public event EventHandler? VerticalToggleRequested;
        public event EventHandler? FontToggleRequested;
        public event EventHandler? SetDefaultFont1Requested;
        public event EventHandler? SetDefaultFont2Requested;
        public event EventHandler? ResetDefaultFontsRequested;
        public event EventHandler? TocRequested;
        public event ItemClickEventHandler? TocItemClicked;
        public event EventHandler? GoToPageRequested;
        public event EventHandler? TextSizeDownRequested;
        public event EventHandler? TextSizeUpRequested;
        public event EventHandler? TextThemeToggleRequested;
        public event EventHandler? SideBySideToggleRequested;
        public event EventHandler? NextImageSideToggleRequested;
        public event EventHandler? SharpenToggleRequested;
        public event EventHandler? SharpenParamsResetRequested;
        public event EventHandler? PreviousFileRequested;
        public event EventHandler? PreviousPageRequested;
        public event EventHandler? NextPageRequested;
        public event EventHandler? NextFileRequested;
        public event EventHandler? FullscreenToggleRequested;
        public event EventHandler? CloseWindowRequested;
        public event EventHandler<BookmarkViewModel>? FavoriteItemClicked;
        public event EventHandler<BookmarkViewModel>? FavoriteRemoveClicked;
        public event EventHandler<BookmarkViewModel>? FavoritePinClicked;
        public event EventHandler<BookmarkViewModel>? RecentItemClicked;
        public event EventHandler<BookmarkViewModel>? RecentRemoveClicked;

        public static readonly DependencyProperty ImageOptionsProperty =
            DependencyProperty.Register(
                nameof(ImageOptions),
                typeof(ImageProcessingViewModel),
                typeof(MainToolbarControl),
                new PropertyMetadata(null, OnImageOptionsChanged));

        public ImageProcessingViewModel? ImageOptions
        {
            get => (ImageProcessingViewModel?)GetValue(ImageOptionsProperty);
            set => SetValue(ImageOptionsProperty, value);
        }

        public MainToolbarControl()
        {
            InitializeComponent();
            ApplyImageOptionsDataContext();
            HookEvents();
        }

        private static void OnImageOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MainToolbarControl control)
            {
                control.ApplyImageOptionsDataContext();
            }
        }

        private void ApplyImageOptionsDataContext()
        {
            SharpenSettingsPanel.DataContext = ImageOptions;
        }

        private void HookEvents()
        {
            ChangeFontMenuItem.Click += (_, _) => ChangeFontRequested?.Invoke(this, EventArgs.Empty);
            EncAutoItem.Click += EncodingItem_Click;
            EncUtf8Item.Click += EncodingItem_Click;
            EncEucKrItem.Click += EncodingItem_Click;
            EncSjisItem.Click += EncodingItem_Click;
            EncJohabItem.Click += EncodingItem_Click;
            ChangeColorsMenuItem.Click += (_, _) => ChangeColorsRequested?.Invoke(this, EventArgs.Empty);
            ChangeUiFontMenuItem.Click += (_, _) => ChangeUiFontRequested?.Invoke(this, EventArgs.Empty);
            LangAutoItem.Click += LanguageItem_Click;
            LangKoItem.Click += LanguageItem_Click;
            LangEnItem.Click += LanguageItem_Click;
            LangJaItem.Click += LanguageItem_Click;
            LangZhHansItem.Click += LanguageItem_Click;
            LangZhHantItem.Click += LanguageItem_Click;
            LangViItem.Click += LanguageItem_Click;
            MatchControlDirectionMenuItem.Click += (_, _) => MatchControlDirectionChanged?.Invoke(this, MatchControlDirectionMenuItem.IsChecked);
            AllowMultipleInstancesMenuItem.Click += (_, _) => AllowMultipleInstancesChanged?.Invoke(this, AllowMultipleInstancesMenuItem.IsChecked);
            AutoDoublePageForArchiveMenuItem.Click += (_, _) => AutoDoublePageForArchiveChanged?.Invoke(this, AutoDoublePageForArchiveMenuItem.IsChecked);
            AboutMenuItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

            GlobalThemeToggleButton.Click += (_, _) => GlobalThemeToggleRequested?.Invoke(this, EventArgs.Empty);
            PinButton.Click += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
            AlwaysOnTopButton.Click += (_, _) => AlwaysOnTopToggleRequested?.Invoke(this, EventArgs.Empty);
            ToggleSidebarButton.Click += (_, _) => ToggleSidebarRequested?.Invoke(this, EventArgs.Empty);
            AddToFavoritesButton.Click += (_, _) => AddToFavoritesRequested?.Invoke(this, EventArgs.Empty);
            OpenFileButton.Click += (_, _) => OpenFileRequested?.Invoke(this, EventArgs.Empty);
            OpenFolderButton.Click += (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

            FileFavoritesList.ItemClicked += (_, item) => FavoriteItemClicked?.Invoke(this, item);
            FileFavoritesList.RemoveClicked += (_, item) => FavoriteRemoveClicked?.Invoke(this, item);
            FileFavoritesList.PinClicked += (_, item) => FavoritePinClicked?.Invoke(this, item);
            FolderFavoritesList.ItemClicked += (_, item) => FavoriteItemClicked?.Invoke(this, item);
            FolderFavoritesList.RemoveClicked += (_, item) => FavoriteRemoveClicked?.Invoke(this, item);
            FolderFavoritesList.PinClicked += (_, item) => FavoritePinClicked?.Invoke(this, item);
            RecentList.ItemClicked += (_, item) => RecentItemClicked?.Invoke(this, item);
            RecentList.RemoveClicked += (_, item) => RecentRemoveClicked?.Invoke(this, item);

            PdfTocButton.Click += (_, _) => PdfTocRequested?.Invoke(this, EventArgs.Empty);
            PdfTocListView.ItemClick += (sender, args) => PdfTocItemClicked?.Invoke(sender, args);
            PdfGoToPageButton.Click += (_, _) => PdfGoToPageRequested?.Invoke(this, EventArgs.Empty);
            PdfGoToPageButton.RightTapped += (sender, args) => SearchRequested?.Invoke(sender, args);
            ZoomOutButton.Click += (_, _) =>
            {
                ZoomLevelChanged?.Invoke(this, -1);
                ZoomOutRequested?.Invoke(this, EventArgs.Empty);
            };
            ZoomInButton.Click += (_, _) =>
            {
                ZoomLevelChanged?.Invoke(this, 1);
                ZoomInRequested?.Invoke(this, EventArgs.Empty);
            };
            ZoomFitButton.Click += (_, _) => ZoomFitRequested?.Invoke(this, EventArgs.Empty);
            ZoomActualButton.Click += (_, _) => ZoomActualRequested?.Invoke(this, EventArgs.Empty);

            AozoraToggleButton.Click += (_, _) => AozoraToggleRequested?.Invoke(this, EventArgs.Empty);
            VerticalToggleButton.Click += (_, _) => VerticalToggleRequested?.Invoke(this, EventArgs.Empty);
            FontToggleButton.Click += (_, _) => FontToggleRequested?.Invoke(this, EventArgs.Empty);
            SetDefaultFont1MenuItem.Click += (_, _) => SetDefaultFont1Requested?.Invoke(this, EventArgs.Empty);
            SetDefaultFont2MenuItem.Click += (_, _) => SetDefaultFont2Requested?.Invoke(this, EventArgs.Empty);
            ResetDefaultFontsMenuItem.Click += (_, _) => ResetDefaultFontsRequested?.Invoke(this, EventArgs.Empty);
            TocButton.Click += (_, _) => TocRequested?.Invoke(this, EventArgs.Empty);
            TocListView.ItemClick += (sender, args) => TocItemClicked?.Invoke(sender, args);
            GoToPageButton.Click += (_, _) => GoToPageRequested?.Invoke(this, EventArgs.Empty);
            GoToPageButton.RightTapped += (sender, args) => SearchRequested?.Invoke(sender, args);
            TextSizeDownButton.Click += (_, _) => TextSizeDownRequested?.Invoke(this, EventArgs.Empty);
            TextSizeUpButton.Click += (_, _) => TextSizeUpRequested?.Invoke(this, EventArgs.Empty);
            ThemeToggleButton.Click += (_, _) => TextThemeToggleRequested?.Invoke(this, EventArgs.Empty);

            SideBySideButton.Click += (_, _) => SideBySideToggleRequested?.Invoke(this, EventArgs.Empty);
            NextImageSideButton.Click += (_, _) => NextImageSideToggleRequested?.Invoke(this, EventArgs.Empty);
            SharpenButton.Click += (_, _) => SharpenToggleRequested?.Invoke(this, EventArgs.Empty);
            SharpenParamsResetButton.Click += (_, _) => SharpenParamsResetRequested?.Invoke(this, EventArgs.Empty);
            PrevFileButton.Click += (_, _) => PreviousFileRequested?.Invoke(this, EventArgs.Empty);
            PrevPageButton.Click += (_, _) => PreviousPageRequested?.Invoke(this, EventArgs.Empty);
            NextPageButton.Click += (_, _) => NextPageRequested?.Invoke(this, EventArgs.Empty);
            NextFileButton.Click += (_, _) => NextFileRequested?.Invoke(this, EventArgs.Empty);
            FullscreenButton.Click += (_, _) => FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);
            CloseWindowButton.Click += (_, _) => CloseWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EncodingItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string tag)
            {
                EncodingSelected?.Invoke(this, tag);
            }
        }

        private void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is string language)
            {
                LanguageSelected?.Invoke(this, language);
            }
        }

        public void ApplyLocalization()
        {
            ToolTipService.SetToolTip(ToggleSidebarButton, Strings.ToggleSidebarTooltip);
            ToolTipService.SetToolTip(OpenFileButton, Strings.OpenFileTooltip);
            ToolTipService.SetToolTip(OpenFolderButton, Strings.OpenFolderTooltip);
            ToolTipService.SetToolTip(ZoomOutButton, Strings.ZoomOutTooltip);
            ToolTipService.SetToolTip(ZoomInButton, Strings.ZoomInTooltip);
            ToolTipService.SetToolTip(ZoomFitButton, Strings.ZoomFitTooltip);
            ToolTipService.SetToolTip(ZoomActualButton, Strings.ZoomActualTooltip);
            ToolTipService.SetToolTip(SharpenButton, Strings.SharpenTooltip);
            ToolTipService.SetToolTip(SideBySideButton, Strings.SideBySideTooltip);
            ToolTipService.SetToolTip(NextImageSideButton, Strings.NextImageSideTooltip);
            ToolTipService.SetToolTip(AozoraToggleButton, Strings.AozoraTooltip);
            ToolTipService.SetToolTip(VerticalToggleButton, Strings.VerticalTooltip);
            ToolTipService.SetToolTip(FontToggleButton, Strings.FontTooltip);
            ToolTipService.SetToolTip(GoToPageButton, Strings.GoToPageTooltip);
            ToolTipService.SetToolTip(TextSizeDownButton, Strings.TextSizeDownTooltip);
            ToolTipService.SetToolTip(TextSizeUpButton, Strings.TextSizeUpTooltip);
            ToolTipService.SetToolTip(ThemeToggleButton, Strings.ThemeTooltip);
            ToolTipService.SetToolTip(FullscreenButton, Strings.FullscreenTooltip);
            ToolTipService.SetToolTip(CloseWindowButton, Strings.CloseWindowTooltip);
            ToolTipService.SetToolTip(RecentButton, Strings.RecentTooltip);
            ToolTipService.SetToolTip(FavoritesButton, Strings.FavoritesTooltip);
            ToolTipService.SetToolTip(TocButton, Strings.TocTooltip);
            ToolTipService.SetToolTip(PdfTocButton, Strings.TocTooltip);
            ToolTipService.SetToolTip(PdfGoToPageButton, Strings.PdfGoToPageTooltip);
            ToolTipService.SetToolTip(SettingsButton, Strings.SettingsTooltip);
            ToolTipService.SetToolTip(PinButton, Strings.PinTooltip);
            ToolTipService.SetToolTip(AlwaysOnTopButton, Strings.AlwaysOnTopTooltip);
            ToolTipService.SetToolTip(PrevFileButton, Strings.PrevFileTooltip);
            ToolTipService.SetToolTip(NextFileButton, Strings.NextFileTooltip);
            ToolTipService.SetToolTip(PrevPageButton, Strings.PrevPageTooltip);
            ToolTipService.SetToolTip(NextPageButton, Strings.NextPageTooltip);

            AddToFavoritesButton.Content = Strings.AddToFavorites;
            ChangeFontMenuItem.Text = Strings.ChangeFont;
            ChangeUiFontMenuItem.Text = Strings.ChangeUiFont;
            EncodingMenuItem.Text = Strings.EncodingMenu;
            EncAutoItem.Text = Strings.EncAuto;
            EncUtf8Item.Text = Strings.EncUtf8;
            EncEucKrItem.Text = Strings.EncEucKr;
            EncSjisItem.Text = Strings.EncSjis;
            EncJohabItem.Text = Strings.EncJohab;
            ChangeColorsMenuItem.Text = Strings.ChangeColors;
            MatchControlDirectionMenuItem.Text = Strings.MatchControlDirection;
            ToolTipService.SetToolTip(MatchControlDirectionMenuItem, Strings.MatchControlDirectionTooltip);
            AllowMultipleInstancesMenuItem.Text = Strings.AllowMultipleInstances;
            ToolTipService.SetToolTip(AllowMultipleInstancesMenuItem, Strings.AllowMultipleInstancesTooltip);
            AutoDoublePageForArchiveMenuItem.Text = Strings.AutoDoublePageForArchive;
            AboutMenuItem.Text = Strings.About;

            LanguageMenuItem.Text = Strings.LanguageSelection;
            LangAutoItem.Text = Strings.LanguageAuto;
            LangKoItem.Text = Strings.LanguageKorean;
            LangEnItem.Text = Strings.LanguageEnglish;
            LangJaItem.Text = Strings.LanguageJapanese;
            LangZhHansItem.Text = Strings.LanguageChineseSimplified;
            LangZhHantItem.Text = Strings.LanguageChineseTraditional;
            LangViItem.Text = Strings.LanguageVietnamese;

            FileFavoritesPivotItem.Header = Strings.FavoritesFiles;
            FolderFavoritesPivotItem.Header = Strings.FavoritesFolders;

            SharpenSettingsTitleText.Text = Strings.SharpenSettingsTitle;
            UpscaleLabel.Text = Strings.UpscaleFactorLabel;
            SharpenAmountLabel.Text = Strings.SharpenAmountLabel;
            SharpenThresholdLabel.Text = Strings.SharpenThresholdLabel;
            UnsharpAmountLabel.Text = Strings.UnsharpAmountLabel;
            UnsharpRadiusLabel.Text = Strings.UnsharpRadiusLabel;
            SharpenParamsResetButton.Content = Strings.ResetButton;

            Bindings.Update();
        }

        public void SetEncodingSelection(string encodingName)
        {
            EncAutoItem.IsChecked = encodingName == "Auto Detect" || encodingName == "Auto";
            EncUtf8Item.IsChecked = encodingName == "UTF-8";
            EncEucKrItem.IsChecked = encodingName == "EUC-KR";
            EncSjisItem.IsChecked = encodingName == "Shift-JIS";
            EncJohabItem.IsChecked = encodingName == "Johab";
        }

        public void SetLanguageSelection(string language)
        {
            var current = string.IsNullOrEmpty(language) ? "Auto" : language;
            LangAutoItem.IsChecked = current == "Auto";
            LangKoItem.IsChecked = current == "ko-KR";
            LangEnItem.IsChecked = current == "en-US";
            LangJaItem.IsChecked = current == "ja-JP";
            LangZhHansItem.IsChecked = current == "zh-Hans";
            LangZhHantItem.IsChecked = current == "zh-Hant";
            LangViItem.IsChecked = current == "vi-VN";
        }

        public void SetWindowOptionStates(
            bool matchControlDirection,
            bool allowMultipleInstances,
            bool autoDoublePageForArchive,
            bool alwaysOnTop)
        {
            MatchControlDirectionMenuItem.IsChecked = matchControlDirection;
            AllowMultipleInstancesMenuItem.IsChecked = allowMultipleInstances;
            AutoDoublePageForArchiveMenuItem.IsChecked = autoDoublePageForArchive;
            AlwaysOnTopButton.IsChecked = alwaysOnTop;
        }

        public void SetMatchControlDirection(bool value) => MatchControlDirectionMenuItem.IsChecked = value;
        public void SetAllowMultipleInstances(bool value) => AllowMultipleInstancesMenuItem.IsChecked = value;
        public void SetAutoDoublePageForArchive(bool value) => AutoDoublePageForArchiveMenuItem.IsChecked = value;
        public void SetAlwaysOnTopState(bool value) => AlwaysOnTopButton.IsChecked = value;

        public void SetAozoraToggleChecked(bool isChecked) => AozoraToggleButton.IsChecked = isChecked;

        public bool IsVerticalToggleChecked => VerticalToggleButton.IsChecked ?? false;

        public void SetVerticalToggleState(bool? isChecked = null, bool? isEnabled = null)
        {
            if (isChecked.HasValue) VerticalToggleButton.IsChecked = isChecked.Value;
            if (isEnabled.HasValue) VerticalToggleButton.IsEnabled = isEnabled.Value;
        }

        public void SetTextSizeLevel(double fontSize) => TextSizeLevelText.Text = fontSize.ToString();

        public void SetZoomLevel(double zoomLevel) => ZoomLevelText.Text = $"{(int)(zoomLevel * 100)}%";

        public void SetImageToolbarVisible(bool isVisible) =>
            ImageToolbarPanel.Visibility = ToVisibility(isVisible);

        public void SetTextToolbarVisible(bool isVisible) =>
            TextToolbarPanel.Visibility = ToVisibility(isVisible);

        public void SetSideBySideToolbarVisible(bool isVisible) =>
            SideBySideToolbarPanel.Visibility = ToVisibility(isVisible);

        public void SetSharpenControlsVisible(bool isVisible)
        {
            var visibility = ToVisibility(isVisible);
            SharpenButton.Visibility = visibility;
            SharpenSeparator.Visibility = visibility;
        }

        public void SetPdfTocVisible(bool isVisible) =>
            PdfTocButton.Visibility = ToVisibility(isVisible);

        public void SetPdfGoToPageVisible(bool isVisible)
        {
            var visibility = ToVisibility(isVisible);
            PdfGoToPageButton.Visibility = visibility;
            PdfSeparator.Visibility = visibility;
        }

        public void SetSharpenState(bool isEnabled) =>
            ImageToolbarStateService.ApplySharpenState(SharpenButton, SharpenIcon, isEnabled);

        public void SetSideBySideState(bool isEnabled) =>
            ImageToolbarStateService.ApplySideBySideState(SideBySideButton, SideBySideText, isEnabled);

        public void SetNextImageSideState(bool nextImageOnRight) =>
            ImageToolbarStateService.ApplyNextImageSideState(NextImageSideText, nextImageOnRight);

        public void SetFullscreenState(bool isFullscreen) =>
            FullscreenIcon.Glyph = isFullscreen ? "\uE73F" : "\uE740";

        public void SetPinState(bool isPinned)
        {
            PinButton.IsChecked = isPinned;
            PinIcon.Glyph = isPinned ? "\uE890" : "\uE77A";
        }

        public void SetThemeState(ElementTheme theme)
        {
            ThemeIcon.Glyph = theme == ElementTheme.Dark ? "\uE706" : "\uE708";
            GlobalThemeToggleButton.IsChecked = theme == ElementTheme.Dark;
            UpdateThemeToggleButtonTooltip(theme);
        }

        public void UpdateThemeToggleButtonTooltip(ElementTheme theme)
        {
            ToolTipService.SetToolTip(
                GlobalThemeToggleButton,
                theme == ElementTheme.Dark ? Strings.LightModeTooltip : Strings.DarkModeTooltip);
        }

        public void SetFavoriteSources(
            ObservableCollection<BookmarkViewModel> fileFavorites,
            ObservableCollection<BookmarkViewModel> folderFavorites,
            string emptyMessage)
        {
            FileFavoritesList.ItemsSource = fileFavorites;
            FileFavoritesList.EmptyMessage = emptyMessage;
            FolderFavoritesList.ItemsSource = folderFavorites;
            FolderFavoritesList.EmptyMessage = emptyMessage;
        }

        public void SetRecentSource(ObservableCollection<BookmarkViewModel> recentItems, string emptyMessage)
        {
            RecentList.ItemsSource = recentItems;
            RecentList.EmptyMessage = emptyMessage;
        }

        public void HideFavoritesFlyout() => FavoritesFlyout.Hide();
        public void HideRecentFlyout() => RecentFlyout.Hide();

        public void SetTextTocTitle(string title) => SetFlyoutTitle(TocFlyout, title);
        public void SetPdfTocTitle(string title) => SetFlyoutTitle(PdfTocFlyout, title);
        public void SetTextTocItems(object itemsSource) => TocListView.ItemsSource = itemsSource;
        public void SetPdfTocItems(object itemsSource) => PdfTocListView.ItemsSource = itemsSource;
        public void HideTextTocFlyout() => TocFlyout.Hide();
        public void HidePdfTocFlyout() => PdfTocFlyout.Hide();

        public void ScrollTextTocIntoView(object item) => ScrollIntoView(TocListView, item);
        public void ScrollPdfTocIntoView(object item) => ScrollIntoView(PdfTocListView, item);

        public void UpdateDefaultFontMenu(string defaultFont1, string defaultFont2)
        {
            SetDefaultFont1MenuItem.Text = $"{Strings.DefaultFont1Label}: {defaultFont1}";
            SetDefaultFont2MenuItem.Text = $"{Strings.DefaultFont2Label}: {defaultFont2}";
            ResetDefaultFontsMenuItem.Text = Strings.ResetButton;
        }

        public void ApplyUiFont(FontFamily fontFamily)
        {
            ZoomLevelText.FontFamily = fontFamily;
            TextSizeLevelText.FontFamily = fontFamily;
            FavoritesPivot.FontFamily = fontFamily;
        }

        public void ShowSearchOverlay(
            SearchOverlayService searchOverlayService,
            bool isPdfMode,
            FrameworkElement? requestedAnchor,
            FrameworkElement fallback)
        {
            var anchor = SearchOverlayService.ResolveAnchor(
                isPdfMode,
                requestedAnchor,
                PdfGoToPageButton,
                ImageToolbarPanel,
                fallback,
                GoToPageButton);

            if (anchor != null)
            {
                searchOverlayService.Show(anchor, fallback);
            }
        }

        private static void SetFlyoutTitle(Flyout flyout, string title)
        {
            if (flyout.Content is Grid grid &&
                grid.Children.Count > 0 &&
                grid.Children[0] is TextBlock textBlock)
            {
                textBlock.Text = title;
            }
        }

        private void ScrollIntoView(ListView listView, object item)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    listView.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                }
                catch
                {
                }
            });
        }

        private static Visibility ToVisibility(bool isVisible) =>
            isVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
