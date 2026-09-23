using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Uviewer.Controls
{
    public sealed partial class ExplorerSidebarControl : UserControl
    {
        private readonly Dictionary<FrameworkElement, ToolbarOverflowItemPresentation> _overflowPresentations = new();
        private bool _isArrangingOverflow;
        private bool _overflowUpdateQueued;
        private bool _driveTreeInitialized;

        internal event EventHandler<string>? FolderTreeNavigationRequested;

        /// <summary>Raised when the mouse back button (XButton1) is pressed over the sidebar.</summary>
        internal event EventHandler? NavigateBackRequested;

        /// <summary>Raised when the mouse forward button (XButton2) is pressed over the sidebar.</summary>
        internal event EventHandler? NavigateForwardRequested;

        public ExplorerSidebarControl()
        {
            try
            {
                InitializeComponent();
                // Initialize ranges in a deterministic order, independent of XBF loading.
                InitializeSlider(ThumbnailSizeSlider, 64, 180, 4, 80);
                InitializeSlider(ImageManagerThumbnailSlider, 64, 180, 4, 80);
                InitializeSlider(SidebarDefaultWidthSlider, 200, 600, 10, 340);
                InitializeSlider(SidebarExpandedWidthSlider, 400, 1200, 10, 760);
            }
            catch (Exception ex)
            {
                // Capture the original exception before the outer XAML loader wraps it
                // as "Cannot create instance of type ExplorerSidebarControl".
                Services.StartupDiagnostics.Record("ExplorerSidebarControl initialization", ex);
                throw;
            }
            Loaded += (_, _) => QueueOverflowUpdate();
            SidebarToolbarRoot.SizeChanged += (_, _) => QueueOverflowUpdate();
            FolderNavigationTree.Expanding += FolderNavigationTree_Expanding;
            FolderNavigationTree.ItemInvoked += FolderNavigationTree_ItemInvoked;
            // ListView/GridView mark pointer presses as handled, so listen even for handled events.
            AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        }

        private static void InitializeSlider(Slider slider, double minimum, double maximum, double step, double value)
        {
            slider.Maximum = maximum;
            slider.Minimum = minimum;
            slider.StepFrequency = step;
            slider.Value = value;
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                return;
            }

            switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
            {
                case Microsoft.UI.Input.PointerUpdateKind.XButton1Pressed:
                    e.Handled = true;
                    NavigateBackRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case Microsoft.UI.Input.PointerUpdateKind.XButton2Pressed:
                    e.Handled = true;
                    NavigateForwardRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void QueueOverflowUpdate()
        {
            if (_isArrangingOverflow || _overflowUpdateQueued)
            {
                return;
            }

            _overflowUpdateQueued = true;
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                _overflowUpdateQueued = false;
                ArrangeOverflow();
            }))
            {
                _overflowUpdateQueued = false;
            }
        }

        internal void RefreshOverflowLabels() => QueueOverflowUpdate();

        internal void SetImageManagerLayout(bool enabled, bool isGridView)
        {
            FolderNavigationColumn.Width = new GridLength(enabled ? 240 : 0);
            FolderNavigationPane.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ImageManagerZoomPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ImageManagerGridView.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            FileListView.Visibility = !enabled && !isGridView ? Visibility.Visible : Visibility.Collapsed;
            FileGridView.Visibility = !enabled && isGridView ? Visibility.Visible : Visibility.Collapsed;

            if (enabled && !_driveTreeInitialized)
            {
                foreach (var drive in Services.FileExplorerService.GetDriveRootItems())
                {
                    FolderNavigationTree.RootNodes.Add(new TreeViewNode
                    {
                        Content = drive,
                        HasUnrealizedChildren = true
                    });
                }

                _driveTreeInitialized = true;
            }
        }

        private async void FolderNavigationTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            var node = args.Node;
            if (!node.HasUnrealizedChildren || node.Content is not Uviewer.Models.FileItem folder) return;

            node.HasUnrealizedChildren = false;
            try
            {
                var children = await Services.FileExplorerService.GetChildFolderItemsAsync(folder.FullPath);
                foreach (var child in children)
                {
                    node.Children.Add(new TreeViewNode
                    {
                        Content = child,
                        HasUnrealizedChildren = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Folder tree expansion failed: {ex.Message}");
            }
        }

        private void FolderNavigationTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var folder = args.InvokedItem as Uviewer.Models.FileItem
                ?? (args.InvokedItem as TreeViewNode)?.Content as Uviewer.Models.FileItem;
            if (folder != null)
            {
                FolderTreeNavigationRequested?.Invoke(this, folder.FullPath);
            }
        }

        internal void ApplyUiFont(FontFamily fontFamily)
        {
            ExplorerFilterTextBox.FontFamily = fontFamily;
            ExplorerFilterKindComboBox.FontFamily = fontFamily;
            foreach (ComboBoxItem option in ExplorerFilterKindComboBox.Items)
            {
                option.FontFamily = fontFamily;
            }
            ExplorerFilterEmptyText.FontFamily = fontFamily;
            ThumbnailSettingsTitleText.FontFamily = fontFamily;
            ThumbnailSizeLabel.FontFamily = fontFamily;
            ThumbnailSizeValueText.FontFamily = fontFamily;
            ThumbnailSizeSlider.FontFamily = fontFamily;
            FolderThumbnailsCheckBox.FontFamily = fontFamily;
            RecursiveImageBrowsingCheckBox.FontFamily = fontFamily;
            FolderNavigationTitleText.FontFamily = fontFamily;
            FolderNavigationTree.FontFamily = fontFamily;
            ImageManagerThumbnailSlider.FontFamily = fontFamily;
            SidebarDefaultWidthLabel.FontFamily = fontFamily;
            SidebarDefaultWidthValueText.FontFamily = fontFamily;
            SidebarDefaultWidthSlider.FontFamily = fontFamily;
            SidebarExpandedWidthLabel.FontFamily = fontFamily;
            SidebarExpandedWidthValueText.FontFamily = fontFamily;
            SidebarExpandedWidthSlider.FontFamily = fontFamily;
            SidebarFileFavoritesHeaderText.FontFamily = fontFamily;
            SidebarFolderFavoritesHeaderText.FontFamily = fontFamily;
            ApplyContextMenuFont(FileListView.ContextFlyout, fontFamily);
            ApplyContextMenuFont(FileGridView.ContextFlyout, fontFamily);
        }

        private static void ApplyContextMenuFont(FlyoutBase? flyout, FontFamily fontFamily)
        {
            if (flyout is not MenuFlyout menuFlyout)
            {
                return;
            }

            foreach (var item in menuFlyout.Items)
            {
                item.FontFamily = fontFamily;

                if (item is MenuFlyoutSubItem subItem)
                {
                    ApplyMenuItemsFont(subItem.Items, fontFamily);
                }
            }
        }

        private static void ApplyMenuItemsFont(
            IEnumerable<MenuFlyoutItemBase> items,
            FontFamily fontFamily)
        {
            foreach (var item in items)
            {
                item.FontFamily = fontFamily;

                if (item is MenuFlyoutSubItem subItem)
                {
                    ApplyMenuItemsFont(subItem.Items, fontFamily);
                }
            }
        }

        private void ArrangeOverflow()
        {
            if (_isArrangingOverflow)
            {
                return;
            }

            _isArrangingOverflow = true;
            try
            {
                var primaryItems = new FrameworkElement[]
                {
                    ToggleViewButton,
                    ParentFolderButton,
                    SidebarFavoritesButton,
                    SidebarRecentButton,
                    BrowseFolderButton,
                    SortByDateButton
                };
                var trailingItems = new FrameworkElement[] { WebDavButton };
                var displayOrder = primaryItems.Concat(trailingItems).ToList();

                RestoreOverflowItems();
                SidebarToolbarOverflowButton.Visibility = Visibility.Collapsed;
                double availableWidth = SidebarToolbarRoot.ActualWidth
                    - SidebarToolbarRoot.Padding.Left
                    - SidebarToolbarRoot.Padding.Right;
                if (availableWidth <= 0 || MeasureToolbarWidth(includeOverflowButton: false) <= availableWidth)
                {
                    return;
                }

                SidebarToolbarOverflowButton.Visibility = Visibility.Visible;
                var candidates = primaryItems
                    .Reverse()
                    .Concat(trailingItems.Reverse())
                    .ToList();

                foreach (FrameworkElement element in candidates)
                {
                    MoveItemToOverflow(element);
                    if (MeasureToolbarWidth(includeOverflowButton: true) <= availableWidth)
                    {
                        break;
                    }
                }

                ToolbarOverflowLayout.OrderOverflowItems(
                    SidebarToolbarOverflowPanel,
                    displayOrder,
                    _overflowPresentations);
            }
            finally
            {
                _isArrangingOverflow = false;
            }
        }

        private double MeasureToolbarWidth(bool includeOverflowButton)
        {
            double width = ToolbarOverflowLayout.MeasurePanel(SidebarToolbarPanel)
                + ToolbarOverflowLayout.MeasurePanel(SidebarTrailingToolbarPanel);
            if (includeOverflowButton)
            {
                width += ToolbarOverflowLayout.MeasureElement(SidebarToolbarOverflowButton);
            }

            return width;
        }

        private void MoveItemToOverflow(FrameworkElement element)
        {
            var presentation = new ToolbarOverflowItemPresentation(element, InvokeOverflowItem);
            presentation.Apply();
            _overflowPresentations[element] = presentation;
        }

        private void InvokeOverflowItem(FrameworkElement element, FrameworkElement placementTarget)
        {
            if (element == SidebarFavoritesButton)
            {
                SidebarFavoritesFlyout.ShowAt(placementTarget);
                return;
            }

            if (element == SidebarRecentButton)
            {
                SidebarRecentFlyout.ShowAt(placementTarget);
                return;
            }

            if (element == WebDavButton)
            {
                WebDavFlyout.ShowAt(placementTarget);
                return;
            }

            ToolbarOverflowLayout.InvokeButton(element);
        }

        private void RestoreOverflowItems()
        {
            foreach (ToolbarOverflowItemPresentation presentation in _overflowPresentations.Values)
            {
                presentation.Restore();
            }

            _overflowPresentations.Clear();
            SidebarToolbarOverflowPanel.Children.Clear();
            SidebarToolbarOverflowButton.Visibility = Visibility.Collapsed;
        }

        internal T GetPart<T>(string name) where T : class
        {
            object? part = name switch
            {
                nameof(ToggleViewButton) => ToggleViewButton,
                nameof(ThumbnailSettingsTitleText) => ThumbnailSettingsTitleText,
                nameof(ThumbnailSizeLabel) => ThumbnailSizeLabel,
                nameof(ThumbnailSizeValueText) => ThumbnailSizeValueText,
                nameof(ThumbnailSizeSlider) => ThumbnailSizeSlider,
                nameof(FolderThumbnailsCheckBox) => FolderThumbnailsCheckBox,
                nameof(RecursiveImageBrowsingCheckBox) => RecursiveImageBrowsingCheckBox,
                nameof(FolderNavigationTree) => FolderNavigationTree,
                nameof(ImageManagerThumbnailSlider) => ImageManagerThumbnailSlider,
                nameof(ImageManagerGridView) => ImageManagerGridView,
                nameof(SidebarDefaultWidthLabel) => SidebarDefaultWidthLabel,
                nameof(SidebarDefaultWidthValueText) => SidebarDefaultWidthValueText,
                nameof(SidebarDefaultWidthSlider) => SidebarDefaultWidthSlider,
                nameof(SidebarExpandedWidthLabel) => SidebarExpandedWidthLabel,
                nameof(SidebarExpandedWidthValueText) => SidebarExpandedWidthValueText,
                nameof(SidebarExpandedWidthSlider) => SidebarExpandedWidthSlider,
                nameof(ParentFolderButton) => ParentFolderButton,
                nameof(SidebarFavoritesButton) => SidebarFavoritesButton,
                nameof(SidebarFavoritesFlyout) => SidebarFavoritesFlyout,
                nameof(SidebarAddToFavoritesButton) => SidebarAddToFavoritesButton,
                nameof(SidebarFavoritesPivot) => SidebarFavoritesPivot,
                nameof(SidebarFileFavoritesPivotItem) => SidebarFileFavoritesPivotItem,
                nameof(SidebarFileFavoritesList) => SidebarFileFavoritesList,
                nameof(SidebarFolderFavoritesPivotItem) => SidebarFolderFavoritesPivotItem,
                nameof(SidebarFolderFavoritesList) => SidebarFolderFavoritesList,
                nameof(SidebarRecentButton) => SidebarRecentButton,
                nameof(SidebarRecentFlyout) => SidebarRecentFlyout,
                nameof(SidebarRecentList) => SidebarRecentList,
                nameof(BrowseFolderButton) => BrowseFolderButton,
                nameof(SortByDateButton) => SortByDateButton,
                nameof(SortIcon) => SortIcon,
                nameof(WebDavButton) => WebDavButton,
                nameof(WebDavFlyout) => WebDavFlyout,
                nameof(WebDavPanel) => WebDavPanel,
                nameof(AddWebDavButton) => AddWebDavButton,
                nameof(CurrentPathBreadcrumb) => CurrentPathBreadcrumb,
                nameof(ExplorerFilterTextBox) => ExplorerFilterTextBox,
                nameof(ExplorerFilterKindComboBox) => ExplorerFilterKindComboBox,
                nameof(ClearExplorerFilterButton) => ClearExplorerFilterButton,
                nameof(ExplorerFilterEmptyText) => ExplorerFilterEmptyText,
                nameof(FileListView) => FileListView,
                nameof(FileGridView) => FileGridView,
                _ => null
            };

            return part as T
                ?? throw new System.InvalidOperationException($"Sidebar part '{name}' was not found.");
        }
    }
}
