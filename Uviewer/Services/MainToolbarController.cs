using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Uviewer.Controls;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class MainToolbarControllerHandlers
    {
        public Action ChangeFont { get; init; } = null!;
        public Func<string, Task> ApplyEncodingAsync { get; init; } = null!;
        public Action ChangeColors { get; init; } = null!;
        public Action ChangeUiFont { get; init; } = null!;
        public Func<Task> SelectExternalProgramAsync { get; init; } = null!;
        public Action SaveToolbarCustomization { get; init; } = null!;
        public Func<string, Task> ApplyLanguageAsync { get; init; } = null!;
        public Action<bool> SetMatchControlDirection { get; init; } = null!;
        public Action<bool> SetAllowMultipleInstances { get; init; } = null!;
        public Action<bool> SetAutoDoublePageForArchive { get; init; } = null!;
        public Func<Task> ShowAboutAsync { get; init; } = null!;
        public Action ToggleGlobalTheme { get; init; } = null!;
        public Action TogglePin { get; init; } = null!;
        public Action ToggleAlwaysOnTop { get; init; } = null!;
        public Action ToggleSidebar { get; init; } = null!;
        public Func<Task> AddToFavoritesAsync { get; init; } = null!;
        public Func<Task> OpenFileAsync { get; init; } = null!;
        public Func<Task> OpenFolderAsync { get; init; } = null!;
        public Func<BookmarkViewModel, Task> HandleFavoriteClickedAsync { get; init; } = null!;
        public Func<BookmarkViewModel, Task> HandleFavoriteRemoveClickedAsync { get; init; } = null!;
        public Func<BookmarkViewModel, Task> HandleFavoritePinClickedAsync { get; init; } = null!;
        public Func<BookmarkViewModel, Task> HandleRecentClickedAsync { get; init; } = null!;
        public Func<BookmarkViewModel, Task> HandleRecentRemoveClickedAsync { get; init; } = null!;
        public Action ShowPdfToc { get; init; } = null!;
        public Action<object?> OpenPdfTocItem { get; init; } = null!;
        public Action ShowGoToPage { get; init; } = null!;
        public RightTappedEventHandler SearchRequested { get; init; } = null!;
        public Action ZoomOut { get; init; } = null!;
        public Action ZoomIn { get; init; } = null!;
        public Action ZoomFit { get; init; } = null!;
        public Action ZoomActual { get; init; } = null!;
        public Action ToggleAozora { get; init; } = null!;
        public Action ToggleVertical { get; init; } = null!;
        public Action ToggleFont { get; init; } = null!;
        public Action RefreshPointerCursor { get; init; } = null!;
        public Action SetDefaultFont1 { get; init; } = null!;
        public Action SetDefaultFont2 { get; init; } = null!;
        public Action ResetDefaultFonts { get; init; } = null!;
        public Action ShowTextToc { get; init; } = null!;
        public ItemClickEventHandler TextTocItemClicked { get; init; } = null!;
        public Action TextSizeDown { get; init; } = null!;
        public Action TextSizeUp { get; init; } = null!;
        public Action ToggleTextTheme { get; init; } = null!;
        public Action ToggleSideBySide { get; init; } = null!;
        public Action ToggleNextImageSide { get; init; } = null!;
        public Func<Task> ToggleSharpeningAsync { get; init; } = null!;
        public Action ResetSharpenParams { get; init; } = null!;
        public Func<Task> NavigatePreviousFileAsync { get; init; } = null!;
        public Func<Task> NavigatePreviousPageAsync { get; init; } = null!;
        public Func<Task> NavigateNextPageAsync { get; init; } = null!;
        public Func<Task> NavigateNextFileAsync { get; init; } = null!;
        public Action ToggleFullscreen { get; init; } = null!;
        public Action CloseWindow { get; init; } = null!;
        public Action<string, string, string> ShowNotification { get; init; } = null!;
    }

    internal sealed class MainToolbarController
    {
        private readonly MainToolbarControl _toolbar;
        private readonly MainToolbarControllerHandlers _handlers;

        public MainToolbarController(MainToolbarControl toolbar, MainToolbarControllerHandlers handlers)
        {
            _toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            HookEvents();
        }

        private void HookEvents()
        {
            _toolbar.ChangeFontRequested += (_, _) => _handlers.ChangeFont();
            _toolbar.EncodingSelected += (_, encoding) => RunAsync(() => _handlers.ApplyEncodingAsync(encoding));
            _toolbar.ChangeColorsRequested += (_, _) => _handlers.ChangeColors();
            _toolbar.ChangeUiFontRequested += (_, _) => _handlers.ChangeUiFont();
            _toolbar.SelectExternalProgramRequested += (_, _) => RunAsync(_handlers.SelectExternalProgramAsync);
            _toolbar.ToolbarCustomizationChanged += (_, _) => _handlers.SaveToolbarCustomization();
            _toolbar.LanguageSelected += (_, language) => RunAsync(() => _handlers.ApplyLanguageAsync(language));
            _toolbar.MatchControlDirectionChanged += (_, isChecked) => _handlers.SetMatchControlDirection(isChecked);
            _toolbar.AllowMultipleInstancesChanged += (_, isChecked) => _handlers.SetAllowMultipleInstances(isChecked);
            _toolbar.AutoDoublePageForArchiveChanged += (_, isChecked) => _handlers.SetAutoDoublePageForArchive(isChecked);
            _toolbar.AboutRequested += (_, _) => RunAsync(_handlers.ShowAboutAsync);

            _toolbar.GlobalThemeToggleRequested += (_, _) => _handlers.ToggleGlobalTheme();
            _toolbar.PinToggleRequested += (_, _) => _handlers.TogglePin();
            _toolbar.AlwaysOnTopToggleRequested += (_, _) => _handlers.ToggleAlwaysOnTop();
            _toolbar.ToggleSidebarRequested += (_, _) => _handlers.ToggleSidebar();
            _toolbar.AddToFavoritesRequested += (_, _) => RunAsync(_handlers.AddToFavoritesAsync);
            _toolbar.OpenFileRequested += (_, _) => RunAsync(_handlers.OpenFileAsync);
            _toolbar.OpenFolderRequested += (_, _) => RunAsync(_handlers.OpenFolderAsync);

            _toolbar.FavoriteItemClicked += (_, item) => RunAsync(() => _handlers.HandleFavoriteClickedAsync(item));
            _toolbar.FavoriteRemoveClicked += (_, item) => RunAsync(() => _handlers.HandleFavoriteRemoveClickedAsync(item));
            _toolbar.FavoritePinClicked += (_, item) => RunAsync(() => _handlers.HandleFavoritePinClickedAsync(item));
            _toolbar.RecentItemClicked += (_, item) => RunAsync(() => _handlers.HandleRecentClickedAsync(item));
            _toolbar.RecentRemoveClicked += (_, item) => RunAsync(() => _handlers.HandleRecentRemoveClickedAsync(item));

            _toolbar.PdfTocRequested += (_, _) => _handlers.ShowPdfToc();
            _toolbar.PdfTocItemClicked += (_, args) => _handlers.OpenPdfTocItem(args.ClickedItem);
            _toolbar.PdfGoToPageRequested += (_, _) => _handlers.ShowGoToPage();
            _toolbar.SearchRequested += _handlers.SearchRequested;

            _toolbar.ZoomOutRequested += (_, _) => _handlers.ZoomOut();
            _toolbar.ZoomInRequested += (_, _) => _handlers.ZoomIn();
            _toolbar.ZoomFitRequested += (_, _) => _handlers.ZoomFit();
            _toolbar.ZoomActualRequested += (_, _) => _handlers.ZoomActual();

            _toolbar.AozoraToggleRequested += (_, _) => _handlers.ToggleAozora();
            _toolbar.VerticalToggleRequested += (_, _) => _handlers.ToggleVertical();
            _toolbar.FontToggleRequested += (_, _) => _handlers.ToggleFont();
            _toolbar.RestorePointerCursorRequested += (_, _) => _handlers.RefreshPointerCursor();
            _toolbar.SetDefaultFont1Requested += (_, _) => _handlers.SetDefaultFont1();
            _toolbar.SetDefaultFont2Requested += (_, _) => _handlers.SetDefaultFont2();
            _toolbar.ResetDefaultFontsRequested += (_, _) => _handlers.ResetDefaultFonts();
            _toolbar.TocRequested += (_, _) => _handlers.ShowTextToc();
            _toolbar.TocItemClicked += _handlers.TextTocItemClicked;
            _toolbar.GoToPageRequested += (_, _) => _handlers.ShowGoToPage();
            _toolbar.TextSizeDownRequested += (_, _) => _handlers.TextSizeDown();
            _toolbar.TextSizeUpRequested += (_, _) => _handlers.TextSizeUp();
            _toolbar.TextThemeToggleRequested += (_, _) => _handlers.ToggleTextTheme();

            _toolbar.SideBySideToggleRequested += (_, _) => _handlers.ToggleSideBySide();
            _toolbar.NextImageSideToggleRequested += (_, _) => _handlers.ToggleNextImageSide();
            _toolbar.SharpenToggleRequested += (_, _) => RunAsync(_handlers.ToggleSharpeningAsync);
            _toolbar.SharpenParamsResetRequested += (_, _) => _handlers.ResetSharpenParams();

            _toolbar.PreviousFileRequested += (_, _) => RunAsync(_handlers.NavigatePreviousFileAsync);
            _toolbar.PreviousPageRequested += (_, _) => RunAsync(_handlers.NavigatePreviousPageAsync);
            _toolbar.NextPageRequested += (_, _) => RunAsync(_handlers.NavigateNextPageAsync);
            _toolbar.NextFileRequested += (_, _) => RunAsync(_handlers.NavigateNextFileAsync);
            _toolbar.FullscreenToggleRequested += (_, _) => _handlers.ToggleFullscreen();
            _toolbar.CloseWindowRequested += (_, _) => _handlers.CloseWindow();
        }

        private async void RunAsync(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toolbar command failed: {ex.Message}");
                _handlers.ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }
    }
}
