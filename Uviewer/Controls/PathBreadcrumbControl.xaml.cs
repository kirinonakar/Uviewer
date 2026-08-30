using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Uviewer.Controls
{
    public sealed partial class PathBreadcrumbControl : UserControl
    {
        private const double DefaultFontSize = 11;
        private const double MinimumFontSize = 8;
        private string _text = string.Empty;
        private IReadOnlyList<BreadcrumbEntry> _items = Array.Empty<BreadcrumbEntry>();
        private bool _isUpdatingLayout;

        public PathBreadcrumbControl()
        {
            InitializeComponent();
            SizeChanged += (_, _) => UpdateResponsiveFontSize();
        }

        public event EventHandler<BreadcrumbNavigationRequestedEventArgs>? NavigationRequested;

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                ToolTipService.SetToolTip(this, _text);
                SetLocalPathOrMessage(_text);
            }
        }

        public void SetWebDavPath(string serverName, string remotePath)
        {
            remotePath = string.IsNullOrWhiteSpace(remotePath) ? "/" : remotePath;
            _text = $"WebDAV: {serverName}{remotePath}";
            ToolTipService.SetToolTip(this, _text);

            var items = new List<BreadcrumbEntry>
            {
                new($"WebDAV: {serverName}", "/", IsWebDav: true)
            };

            string currentPath = string.Empty;
            foreach (string segment in remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath += "/" + segment;
                items.Add(new BreadcrumbEntry(segment, currentPath + "/", IsWebDav: true));
            }

            SetItems(items);
        }

        private void SetLocalPathOrMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                SetItems(new[] { new BreadcrumbEntry(value, null, IsWebDav: false) });
                return;
            }

            string normalizedPath = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(value) ?? string.Empty;
            var items = new List<BreadcrumbEntry>();

            string rootLabel = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(rootLabel))
            {
                rootLabel = root;
            }

            items.Add(new BreadcrumbEntry(rootLabel, root, IsWebDav: false));

            string relative = normalizedPath.Length >= root.Length
                ? normalizedPath[root.Length..]
                : string.Empty;
            string currentPath = root;
            foreach (string segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, segment);
                items.Add(new BreadcrumbEntry(segment, currentPath, IsWebDav: false));
            }

            SetItems(items);
        }

        private void SetItems(IReadOnlyList<BreadcrumbEntry> items)
        {
            for (int index = 0; index < items.Count; index++)
            {
                items[index].DisplayLabel = items[index].Label;
                items[index].SeparatorVisibility = index == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            _items = items;
            Breadcrumb.ItemsSource = items;
            UpdateResponsiveFontSize();
        }

        private void UpdateResponsiveFontSize()
        {
            if (_isUpdatingLayout)
            {
                return;
            }

            double availableWidth = Breadcrumb.ActualWidth > 0
                ? Breadcrumb.ActualWidth
                : ActualWidth - 24;
            if (availableWidth <= 0 || _items.Count == 0)
            {
                return;
            }

            int characterCount = _items.Sum(item => item.DisplayLabel.Length);
            double fixedItemWidth = _items.Count * 8;
            double widthPerFontUnit = characterCount * 0.58;
            double fontSize = widthPerFontUnit <= 0
                ? DefaultFontSize
                : ((availableWidth * 2) - fixedItemWidth) / widthPerFontUnit;

            fontSize = Math.Clamp(fontSize, MinimumFontSize, DefaultFontSize);

            int firstVisibleIndex = 0;
            double maximumContentWidth = availableWidth * 2;
            double visibleWidth = _items.Sum(item => EstimateItemWidth(item, fontSize));
            double ellipsisWidth = EstimateItemWidth(
                new BreadcrumbEntry("…", null, IsWebDav: false),
                fontSize);

            if (visibleWidth > maximumContentWidth)
            {
                while (firstVisibleIndex < _items.Count - 1)
                {
                    visibleWidth -= EstimateItemWidth(_items[firstVisibleIndex], fontSize);
                    firstVisibleIndex++;

                    if (visibleWidth + ellipsisWidth <= maximumContentWidth)
                    {
                        break;
                    }
                }
            }

            IReadOnlyList<BreadcrumbEntry> visibleItems = _items;
            if (firstVisibleIndex > 0)
            {
                var collapsedItems = new List<BreadcrumbEntry>
                {
                    new("…", null, IsWebDav: false)
                    {
                        DisplayLabel = "…",
                        SeparatorVisibility = Visibility.Collapsed
                    }
                };
                collapsedItems.AddRange(_items.Skip(firstVisibleIndex));
                visibleItems = collapsedItems;
            }

            _isUpdatingLayout = true;
            try
            {
                Breadcrumb.FontSize = fontSize;
                Breadcrumb.ItemsSource = visibleItems;
            }
            finally
            {
                _isUpdatingLayout = false;
            }
        }

        private static double EstimateItemWidth(BreadcrumbEntry item, double fontSize)
        {
            double separatorWidth = item.SeparatorVisibility == Visibility.Visible
                ? (fontSize * 0.58) + 4
                : 0;
            return Math.Min(220, (item.DisplayLabel.Length * fontSize * 0.58) + 8 + separatorWidth);
        }

        private void BreadcrumbItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: BreadcrumbEntry { Target: not null } item })
            {
                NavigationRequested?.Invoke(
                    this,
                    new BreadcrumbNavigationRequestedEventArgs(item.Target, item.IsWebDav));
            }
        }

        private sealed record BreadcrumbEntry(string Label, string? Target, bool IsWebDav)
        {
            public string DisplayLabel { get; set; } = Label;
            public Visibility SeparatorVisibility { get; set; } = Visibility.Collapsed;
        }
    }

    public sealed class BreadcrumbNavigationRequestedEventArgs : EventArgs
    {
        public BreadcrumbNavigationRequestedEventArgs(string target, bool isWebDav)
        {
            Target = target;
            IsWebDav = isWebDav;
        }

        public string Target { get; }
        public bool IsWebDav { get; }
    }
}
