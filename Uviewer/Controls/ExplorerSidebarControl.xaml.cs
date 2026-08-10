using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace Uviewer.Controls
{
    public sealed partial class ExplorerSidebarControl : UserControl
    {
        private readonly Dictionary<FrameworkElement, ToolbarOverflowItemPresentation> _overflowPresentations = new();
        private bool _isArrangingOverflow;
        private bool _overflowUpdateQueued;

        public ExplorerSidebarControl()
        {
            InitializeComponent();
            Loaded += (_, _) => QueueOverflowUpdate();
            SidebarToolbarRoot.SizeChanged += (_, _) => QueueOverflowUpdate();
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
                nameof(CurrentPathText) => CurrentPathText,
                nameof(FileListView) => FileListView,
                nameof(FileGridView) => FileGridView,
                _ => null
            };

            return part as T
                ?? throw new System.InvalidOperationException($"Sidebar part '{name}' was not found.");
        }
    }
}
