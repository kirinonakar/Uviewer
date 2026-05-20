using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Uviewer.Models;

namespace Uviewer.Controls
{
    public sealed partial class MainToolbarControl : UserControl
    {
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

        internal T GetPart<T>(string name) where T : class
        {
            object? part = name switch
            {
                nameof(SettingsButton) => SettingsButton,
                nameof(ChangeFontMenuItem) => ChangeFontMenuItem,
                nameof(EncodingMenuItem) => EncodingMenuItem,
                nameof(EncAutoItem) => EncAutoItem,
                nameof(EncUtf8Item) => EncUtf8Item,
                nameof(EncEucKrItem) => EncEucKrItem,
                nameof(EncSjisItem) => EncSjisItem,
                nameof(EncJohabItem) => EncJohabItem,
                nameof(ChangeColorsMenuItem) => ChangeColorsMenuItem,
                nameof(ChangeUiFontMenuItem) => ChangeUiFontMenuItem,
                nameof(LanguageMenuItem) => LanguageMenuItem,
                nameof(LangAutoItem) => LangAutoItem,
                nameof(LangKoItem) => LangKoItem,
                nameof(LangEnItem) => LangEnItem,
                nameof(LangJaItem) => LangJaItem,
                nameof(LangZhHansItem) => LangZhHansItem,
                nameof(LangZhHantItem) => LangZhHantItem,
                nameof(LangViItem) => LangViItem,
                nameof(MatchControlDirectionMenuItem) => MatchControlDirectionMenuItem,
                nameof(AllowMultipleInstancesMenuItem) => AllowMultipleInstancesMenuItem,
                nameof(AutoDoublePageForArchiveMenuItem) => AutoDoublePageForArchiveMenuItem,
                nameof(AboutMenuItem) => AboutMenuItem,
                nameof(GlobalThemeToggleButton) => GlobalThemeToggleButton,
                nameof(ThemeIcon) => ThemeIcon,
                nameof(PinButton) => PinButton,
                nameof(PinIcon) => PinIcon,
                nameof(AlwaysOnTopButton) => AlwaysOnTopButton,
                nameof(AlwaysOnTopIcon) => AlwaysOnTopIcon,
                nameof(ToggleSidebarButton) => ToggleSidebarButton,
                nameof(FavoritesButton) => FavoritesButton,
                nameof(FavoritesFlyout) => FavoritesFlyout,
                nameof(AddToFavoritesButton) => AddToFavoritesButton,
                nameof(FavoritesPivot) => FavoritesPivot,
                nameof(FileFavoritesPivotItem) => FileFavoritesPivotItem,
                nameof(FileFavoritesList) => FileFavoritesList,
                nameof(FolderFavoritesPivotItem) => FolderFavoritesPivotItem,
                nameof(FolderFavoritesList) => FolderFavoritesList,
                nameof(RecentButton) => RecentButton,
                nameof(RecentFlyout) => RecentFlyout,
                nameof(RecentList) => RecentList,
                nameof(OpenFileButton) => OpenFileButton,
                nameof(OpenFolderButton) => OpenFolderButton,
                nameof(ImageToolbarPanel) => ImageToolbarPanel,
                nameof(PdfTocButton) => PdfTocButton,
                nameof(PdfTocFlyout) => PdfTocFlyout,
                nameof(PdfTocListView) => PdfTocListView,
                nameof(PdfGoToPageButton) => PdfGoToPageButton,
                nameof(PdfSeparator) => PdfSeparator,
                nameof(ZoomOutButton) => ZoomOutButton,
                nameof(ZoomLevelText) => ZoomLevelText,
                nameof(ZoomInButton) => ZoomInButton,
                nameof(ZoomFitButton) => ZoomFitButton,
                nameof(ZoomActualButton) => ZoomActualButton,
                nameof(TextToolbarPanel) => TextToolbarPanel,
                nameof(AozoraToggleButton) => AozoraToggleButton,
                nameof(VerticalToggleButton) => VerticalToggleButton,
                nameof(FontToggleButton) => FontToggleButton,
                nameof(SetDefaultFont1MenuItem) => SetDefaultFont1MenuItem,
                nameof(SetDefaultFont2MenuItem) => SetDefaultFont2MenuItem,
                nameof(ResetDefaultFontsMenuItem) => ResetDefaultFontsMenuItem,
                nameof(TocButton) => TocButton,
                nameof(TocFlyout) => TocFlyout,
                nameof(TocListView) => TocListView,
                nameof(GoToPageButton) => GoToPageButton,
                nameof(TextSizeDownButton) => TextSizeDownButton,
                nameof(TextSizeLevelText) => TextSizeLevelText,
                nameof(TextSizeUpButton) => TextSizeUpButton,
                nameof(ThemeToggleButton) => ThemeToggleButton,
                nameof(SideBySideToolbarPanel) => SideBySideToolbarPanel,
                nameof(SideBySideButton) => SideBySideButton,
                nameof(SideBySideText) => SideBySideText,
                nameof(NextImageSideButton) => NextImageSideButton,
                nameof(NextImageSideText) => NextImageSideText,
                nameof(SharpenSeparator) => SharpenSeparator,
                nameof(SharpenButton) => SharpenButton,
                nameof(SharpenSettingsPanel) => SharpenSettingsPanel,
                nameof(SharpenSettingsTitleText) => SharpenSettingsTitleText,
                nameof(UpscaleLabel) => UpscaleLabel,
                nameof(SharpenAmountLabel) => SharpenAmountLabel,
                nameof(SharpenThresholdLabel) => SharpenThresholdLabel,
                nameof(UnsharpAmountLabel) => UnsharpAmountLabel,
                nameof(UnsharpRadiusLabel) => UnsharpRadiusLabel,
                nameof(SharpenParamsResetButton) => SharpenParamsResetButton,
                nameof(SharpenIcon) => SharpenIcon,
                nameof(PrevFileButton) => PrevFileButton,
                nameof(PrevPageButton) => PrevPageButton,
                nameof(NextPageButton) => NextPageButton,
                nameof(NextFileButton) => NextFileButton,
                nameof(FullscreenButton) => FullscreenButton,
                nameof(FullscreenIcon) => FullscreenIcon,
                nameof(CloseWindowButton) => CloseWindowButton,
                _ => null
            };

            return part as T
                ?? throw new System.InvalidOperationException($"Toolbar part '{name}' was not found.");
        }
    }
}
