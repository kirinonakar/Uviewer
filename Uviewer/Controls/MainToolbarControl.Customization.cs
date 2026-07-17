using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Controls
{
    public sealed partial class MainToolbarControl
    {
        private readonly Dictionary<string, FrameworkElement> _toolbarItems = new(StringComparer.Ordinal);
        private AppToolbarSettings _toolbarSettings = AppToolbarSettings.CreateDefault();
        private ContentDialog? _toolbarCustomizationDialog;
        private bool _isToolbarLayoutInitialized;
        private bool _isImageToolbarAvailable = true;
        private bool _isTextToolbarAvailable;
        private bool _isSideBySideToolbarAvailable = true;
        private bool _isSharpenAvailable = true;
        private bool _isPdfTocAvailable;
        private bool _isPdfGoToPageAvailable;

        private static readonly HashSet<string> ImageToolbarItemIds = new(StringComparer.Ordinal)
        {
            ToolbarItemIds.ZoomOut,
            ToolbarItemIds.ZoomIn,
            ToolbarItemIds.ZoomFit,
            ToolbarItemIds.ZoomActual
        };

        private static readonly HashSet<string> TextToolbarItemIds = new(StringComparer.Ordinal)
        {
            ToolbarItemIds.Aozora,
            ToolbarItemIds.Vertical,
            ToolbarItemIds.Font,
            ToolbarItemIds.TextToc,
            ToolbarItemIds.GoToPage,
            ToolbarItemIds.TextSizeDown,
            ToolbarItemIds.TextSizeUp,
            ToolbarItemIds.TextTheme
        };

        private static readonly HashSet<string> SideBySideToolbarItemIds = new(StringComparer.Ordinal)
        {
            ToolbarItemIds.SideBySide,
            ToolbarItemIds.NextImageSide
        };

        private void InitializeToolbarCustomization()
        {
            _toolbarItems[ToolbarItemIds.Settings] = SettingsButton;
            _toolbarItems[ToolbarItemIds.GlobalTheme] = GlobalThemeToggleButton;
            _toolbarItems[ToolbarItemIds.Pin] = PinButton;
            _toolbarItems[ToolbarItemIds.AlwaysOnTop] = AlwaysOnTopButton;
            _toolbarItems[ToolbarItemIds.ToggleSidebar] = ToggleSidebarButton;
            _toolbarItems[ToolbarItemIds.Favorites] = FavoritesButton;
            _toolbarItems[ToolbarItemIds.Recent] = RecentButton;
            _toolbarItems[ToolbarItemIds.OpenFile] = OpenFileButton;
            _toolbarItems[ToolbarItemIds.OpenFolder] = OpenFolderButton;
            _toolbarItems[ToolbarItemIds.PdfToc] = PdfTocButton;
            _toolbarItems[ToolbarItemIds.PdfGoToPage] = PdfGoToPageButton;
            _toolbarItems[ToolbarItemIds.ZoomOut] = ZoomOutButton;
            _toolbarItems[ToolbarItemIds.ZoomIn] = ZoomInButton;
            _toolbarItems[ToolbarItemIds.ZoomFit] = ZoomFitButton;
            _toolbarItems[ToolbarItemIds.ZoomActual] = ZoomActualButton;
            _toolbarItems[ToolbarItemIds.Aozora] = AozoraToggleButton;
            _toolbarItems[ToolbarItemIds.Vertical] = VerticalToggleButton;
            _toolbarItems[ToolbarItemIds.Font] = FontToggleButton;
            _toolbarItems[ToolbarItemIds.TextToc] = TocButton;
            _toolbarItems[ToolbarItemIds.GoToPage] = GoToPageButton;
            _toolbarItems[ToolbarItemIds.TextSizeDown] = TextSizeDownButton;
            _toolbarItems[ToolbarItemIds.TextSizeUp] = TextSizeUpButton;
            _toolbarItems[ToolbarItemIds.TextTheme] = ThemeToggleButton;
            _toolbarItems[ToolbarItemIds.SideBySide] = SideBySideButton;
            _toolbarItems[ToolbarItemIds.NextImageSide] = NextImageSideButton;
            _toolbarItems[ToolbarItemIds.Sharpen] = SharpenButton;
            _toolbarItems[ToolbarItemIds.PreviousFile] = PrevFileButton;
            _toolbarItems[ToolbarItemIds.PreviousPage] = PrevPageButton;
            _toolbarItems[ToolbarItemIds.NextPage] = NextPageButton;
            _toolbarItems[ToolbarItemIds.NextFile] = NextFileButton;
            _toolbarItems[ToolbarItemIds.Fullscreen] = FullscreenButton;
            _toolbarItems[ToolbarItemIds.CloseWindow] = CloseWindowButton;

            _toolbarSettings = NormalizeToolbarSettings(AppToolbarSettings.CreateDefault());
            Loaded += MainToolbarControl_Loaded;
        }

        public AppToolbarSettings GetToolbarSettings() => _toolbarSettings.Clone();

        public void ApplyToolbarSettings(AppToolbarSettings? settings)
        {
            _toolbarSettings = NormalizeToolbarSettings(settings);
            if (_isToolbarLayoutInitialized)
            {
                RebuildToolbarPanels();
            }
        }

        private void MainToolbarControl_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainToolbarControl_Loaded;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isToolbarLayoutInitialized) return;

                _isToolbarLayoutInitialized = true;
                RebuildToolbarPanels();
            });
        }

        private static AppToolbarSettings NormalizeToolbarSettings(AppToolbarSettings? settings)
        {
            var source = settings?.Clone() ?? AppToolbarSettings.CreateDefault();
            var known = new HashSet<string>(ToolbarItemIds.All, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new AppToolbarSettings();

            foreach (var id in source.LeftItems ?? new List<string>())
            {
                if (known.Contains(id) && used.Add(id)) normalized.LeftItems.Add(id);
            }

            foreach (var id in source.RightItems ?? new List<string>())
            {
                if (known.Contains(id) && used.Add(id)) normalized.RightItems.Add(id);
            }

            if (!used.Contains(ToolbarItemIds.Settings))
            {
                normalized.LeftItems.Insert(0, ToolbarItemIds.Settings);
                used.Add(ToolbarItemIds.Settings);
            }

            var defaultLeft = new HashSet<string>(AppToolbarSettings.CreateDefault().LeftItems, StringComparer.Ordinal);
            foreach (var id in ToolbarItemIds.All)
            {
                if (used.Add(id))
                {
                    (defaultLeft.Contains(id) ? normalized.LeftItems : normalized.RightItems).Add(id);
                }
            }

            normalized.HiddenItems = (source.HiddenItems ?? new List<string>())
                .Where(id => known.Contains(id) && id != ToolbarItemIds.Settings)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return normalized;
        }

        private void RebuildToolbarPanels()
        {
            foreach (var element in _toolbarItems.Values)
            {
                DetachFromParent(element);
            }
            DetachFromParent(ZoomLevelText);
            DetachFromParent(TextSizeLevelText);

            LeftToolbarPanel.Children.Clear();
            RightToolbarPanel.Children.Clear();

            AddToolbarItems(LeftToolbarPanel, _toolbarSettings.LeftItems);
            AddToolbarItems(RightToolbarPanel, _toolbarSettings.RightItems);
            UpdateToolbarItemVisibility();
        }

        private void AddToolbarItems(Panel panel, IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (!_toolbarItems.TryGetValue(id, out var element)) continue;
                panel.Children.Add(element);

                if (id == ToolbarItemIds.ZoomOut)
                {
                    panel.Children.Add(ZoomLevelText);
                }
                else if (id == ToolbarItemIds.TextSizeDown)
                {
                    panel.Children.Add(TextSizeLevelText);
                }
            }
        }

        private static void DetachFromParent(UIElement element)
        {
            if (VisualTreeHelper.GetParent(element) is Panel parent)
            {
                parent.Children.Remove(element);
            }
        }

        private void UpdateToolbarItemVisibility()
        {
            if (_toolbarItems.Count == 0) return;

            var hidden = new HashSet<string>(_toolbarSettings.HiddenItems, StringComparer.Ordinal);
            foreach (var pair in _toolbarItems)
            {
                bool userVisible = pair.Key == ToolbarItemIds.Settings || !hidden.Contains(pair.Key);
                pair.Value.Visibility = userVisible && IsToolbarItemAvailable(pair.Key)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            bool zoomLevelVisible = _isImageToolbarAvailable &&
                (!hidden.Contains(ToolbarItemIds.ZoomOut) || !hidden.Contains(ToolbarItemIds.ZoomIn));
            ZoomLevelText.Visibility = zoomLevelVisible ? Visibility.Visible : Visibility.Collapsed;

            bool textSizeLevelVisible = _isTextToolbarAvailable &&
                (!hidden.Contains(ToolbarItemIds.TextSizeDown) || !hidden.Contains(ToolbarItemIds.TextSizeUp));
            TextSizeLevelText.Visibility = textSizeLevelVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool IsToolbarItemAvailable(string id)
        {
            if (ImageToolbarItemIds.Contains(id)) return _isImageToolbarAvailable;
            if (TextToolbarItemIds.Contains(id)) return _isTextToolbarAvailable;
            if (SideBySideToolbarItemIds.Contains(id)) return _isSideBySideToolbarAvailable;
            if (id == ToolbarItemIds.Sharpen) return _isSharpenAvailable;
            if (id == ToolbarItemIds.PdfToc) return _isPdfTocAvailable;
            if (id == ToolbarItemIds.PdfGoToPage) return _isPdfGoToPageAvailable;
            return true;
        }

        private async Task ShowToolbarCustomizationDialogAsync()
        {
            if (_toolbarCustomizationDialog != null || XamlRoot == null) return;

            var working = _toolbarSettings.Clone();
            double customizationWidth = Math.Clamp(
                XamlRoot.Size.Width - 80,
                380,
                560);
            double listHeight = Math.Clamp(XamlRoot.Size.Height - 330, 240, 460);
            const double transferColumnWidth = 44;
            double paneWidth = (customizationWidth - transferColumnWidth) / 2;

            var leftList = CreateToolbarListView(paneWidth, listHeight);
            var rightList = CreateToolbarListView(paneWidth, listHeight);
            PopulateToolbarList(leftList, working.LeftItems, working);
            PopulateToolbarList(rightList, working.RightItems, working);

            var moveRightButton = CreateTransferButton("→", Strings.ToolbarMoveRight);
            var moveLeftButton = CreateTransferButton("←", Strings.ToolbarMoveLeft);
            moveRightButton.IsEnabled = false;
            moveLeftButton.IsEnabled = false;

            bool changingSelection = false;
            leftList.SelectionChanged += (_, _) =>
            {
                if (changingSelection) return;
                changingSelection = true;
                if (leftList.SelectedItem != null) rightList.SelectedItem = null;
                moveRightButton.IsEnabled = leftList.SelectedItem != null;
                moveLeftButton.IsEnabled = false;
                changingSelection = false;
            };
            rightList.SelectionChanged += (_, _) =>
            {
                if (changingSelection) return;
                changingSelection = true;
                if (rightList.SelectedItem != null) leftList.SelectedItem = null;
                moveLeftButton.IsEnabled = rightList.SelectedItem != null;
                moveRightButton.IsEnabled = false;
                changingSelection = false;
            };

            moveRightButton.Click += (_, _) => MoveSelectedToolbarItem(leftList, rightList, working);
            moveLeftButton.Click += (_, _) => MoveSelectedToolbarItem(rightList, leftList, working);

            var transferButtons = new StackPanel
            {
                Width = transferColumnWidth,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            transferButtons.Children.Add(moveRightButton);
            transferButtons.Children.Add(moveLeftButton);

            var listsGrid = new Grid
            {
                Width = customizationWidth,
                ColumnSpacing = 0
            };
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(paneWidth) });
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(transferColumnWidth) });
            listsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(paneWidth) });

            var leftPane = CreateToolbarPane(Strings.ToolbarLeft, leftList, paneWidth);
            var rightPane = CreateToolbarPane(Strings.ToolbarRight, rightList, paneWidth);
            Grid.SetColumn(transferButtons, 1);
            Grid.SetColumn(rightPane, 2);
            listsGrid.Children.Add(leftPane);
            listsGrid.Children.Add(transferButtons);
            listsGrid.Children.Add(rightPane);

            var resetButton = new Button
            {
                Content = Strings.ResetButton,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            resetButton.Click += (_, _) =>
            {
                working = AppToolbarSettings.CreateDefault();
                PopulateToolbarList(leftList, working.LeftItems, working);
                PopulateToolbarList(rightList, working.RightItems, working);
                moveRightButton.IsEnabled = false;
                moveLeftButton.IsEnabled = false;
            };

            var content = new StackPanel
            {
                Width = customizationWidth,
                Spacing = 12
            };
            content.Children.Add(new TextBlock
            {
                Text = Strings.ToolbarCustomizationDescription,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            });
            content.Children.Add(listsGrid);
            content.Children.Add(resetButton);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = Strings.ToolbarCustomization,
                Content = content,
                PrimaryButtonText = Strings.ToolbarApply,
                CloseButtonText = Strings.DialogClose,
                DefaultButton = ContentDialogButton.Primary
            };

            _toolbarCustomizationDialog = dialog;
            try
            {
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    working.LeftItems = GetToolbarItemOrder(leftList);
                    working.RightItems = GetToolbarItemOrder(rightList);
                    ApplyToolbarSettings(working);
                    ToolbarCustomizationChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                _toolbarCustomizationDialog = null;
            }
        }

        private static ListView CreateToolbarListView(double width, double height) => new()
        {
            Width = width,
            Height = height,
            SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true,
            CanReorderItems = true,
            AllowDrop = true,
            ReorderMode = ListViewReorderMode.Enabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private void PopulateToolbarList(
            ListView listView,
            IEnumerable<string> ids,
            AppToolbarSettings settings)
        {
            listView.Items.Clear();
            foreach (var id in ids)
            {
                listView.Items.Add(CreateToolbarListItem(id, settings));
            }
        }

        private ListViewItem CreateToolbarListItem(
            string id,
            AppToolbarSettings settings,
            bool isSelected = false)
        {
            var visibleCheckBox = new CheckBox
            {
                IsChecked = id == ToolbarItemIds.Settings || !settings.HiddenItems.Contains(id),
                IsEnabled = id != ToolbarItemIds.Settings,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 26,
                MinHeight = 26,
                Padding = new Thickness(0),
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 }
            };
            ToolTipService.SetToolTip(visibleCheckBox, Strings.ToolbarVisible);
            visibleCheckBox.Checked += (_, _) => settings.HiddenItems.Remove(id);
            visibleCheckBox.Unchecked += (_, _) =>
            {
                if (id != ToolbarItemIds.Settings && !settings.HiddenItems.Contains(id))
                {
                    settings.HiddenItems.Add(id);
                }
            };

            var title = new TextBlock
            {
                Text = GetToolbarItemDisplayName(id),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13
            };
            ToolTipService.SetToolTip(title, title.Text);
            Grid.SetColumn(title, 1);

            var dragHandle = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 12,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(dragHandle, 2);

            var row = new Grid { ColumnSpacing = 4, MinHeight = 28 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.Children.Add(visibleCheckBox);
            row.Children.Add(title);
            row.Children.Add(dragHandle);

            return new ListViewItem
            {
                Tag = id,
                Content = row,
                IsSelected = isSelected,
                MinHeight = 30,
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private static Grid CreateToolbarPane(string title, ListView listView, double width)
        {
            var pane = new Grid { Width = width, RowSpacing = 4 };
            pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pane.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            });
            Grid.SetRow(listView, 1);
            pane.Children.Add(listView);
            return pane;
        }

        private static Button CreateTransferButton(string content, string tooltip)
        {
            var button = new Button
            {
                Content = content,
                Width = 34,
                Height = 32,
                MinWidth = 34,
                MinHeight = 32,
                Padding = new Thickness(0),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(button, tooltip);
            return button;
        }

        private void MoveSelectedToolbarItem(
            ListView source,
            ListView target,
            AppToolbarSettings settings)
        {
            if (source.SelectedItem is not ListViewItem selected || selected.Tag is not string id) return;

            source.Items.Remove(selected);
            var movedItem = CreateToolbarListItem(id, settings, isSelected: true);
            target.Items.Add(movedItem);
            target.SelectedItem = movedItem;
            target.ScrollIntoView(movedItem);
        }

        private static List<string> GetToolbarItemOrder(ListView listView)
        {
            var result = new List<string>();
            foreach (var item in listView.Items)
            {
                if (item is ListViewItem listViewItem && listViewItem.Tag is string id)
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private string GetToolbarItemDisplayName(string id)
        {
            if (_toolbarItems.TryGetValue(id, out var element) &&
                ToolTipService.GetToolTip(element) is object tooltip &&
                !string.IsNullOrWhiteSpace(tooltip.ToString()))
            {
                return tooltip.ToString()!;
            }

            return id switch
            {
                ToolbarItemIds.Settings => Strings.SettingsTooltip,
                ToolbarItemIds.GlobalTheme => Strings.ThemeTooltip,
                _ => id
            };
        }
    }
}
