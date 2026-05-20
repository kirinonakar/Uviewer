using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Uviewer.Services
{
    public sealed class SearchOverlayService : IDisposable
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<DocumentSearchMatch>>> _searchAsync;
        private readonly Func<DocumentSearchMatch, Task> _navigateAsync;
        private readonly Func<long> _currentPositionProvider;
        private readonly Action<string?>? _queryChanged;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounceTimer;

        private Flyout? _flyout;
        private TextBox? _searchBox;
        private TextBlock? _statusText;
        private TextBlock? _previewText;
        private Button? _previousButton;
        private Button? _nextButton;
        private CancellationTokenSource? _searchCts;
        private Task? _refreshTask;
        private readonly SemaphoreSlim _navigateLock = new(1, 1);
        private List<DocumentSearchMatch> _matches = new();
        private int _currentIndex = -1;
        private bool _hasNavigatedCurrentQuery;
        private bool _isSearching;
        private int _searchRequestId;

        public SearchOverlayService(
            Func<string, CancellationToken, Task<IReadOnlyList<DocumentSearchMatch>>> searchAsync,
            Func<DocumentSearchMatch, Task> navigateAsync,
            Func<long> currentPositionProvider,
            Action<string?>? queryChanged = null)
        {
            _searchAsync = searchAsync;
            _navigateAsync = navigateAsync;
            _currentPositionProvider = currentPositionProvider;
            _queryChanged = queryChanged;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _debounceTimer = _dispatcherQueue.CreateTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick += async (_, _) => await RefreshMatchesAsync();
        }

        public bool IsOpen => _flyout != null;

        internal static FrameworkElement? ResolveAnchor(
            bool isPdfMode,
            FrameworkElement? requestedAnchor,
            FrameworkElement? pdfGoToPageButton,
            FrameworkElement? imageToolbarPanel,
            FrameworkElement fallback,
            FrameworkElement? textGoToPageButton)
        {
            if (!isPdfMode)
            {
                return requestedAnchor ?? textGoToPageButton;
            }

            if (IsVisibleAnchor(requestedAnchor)) return requestedAnchor;
            if (IsVisibleAnchor(pdfGoToPageButton)) return pdfGoToPageButton;
            if (IsVisibleAnchor(imageToolbarPanel)) return imageToolbarPanel;
            return fallback;
        }

        public void Show(FrameworkElement anchor, FrameworkElement? placementRoot = null)
        {
            EnsureFlyout();
            if (_flyout == null || _searchBox == null) return;

            if (placementRoot != null && placementRoot.ActualWidth > 0)
            {
                double y = 0;
                if (!ReferenceEquals(anchor, placementRoot) &&
                    IsVisibleAnchor(anchor))
                {
                    try
                    {
                        var anchorBottom = anchor.TransformToVisual(placementRoot)
                            .TransformPoint(new Point(0, anchor.ActualHeight));
                        y = Math.Max(0, anchorBottom.Y);
                    }
                    catch
                    {
                        y = 0;
                    }
                }

                var options = new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
                    Position = new Point(Math.Max(0, placementRoot.ActualWidth - 10), y)
                };
                _flyout.ShowAt(placementRoot, options);
            }
            else
            {
                _flyout.ShowAt(anchor);
            }

            _searchBox.Focus(FocusState.Programmatic);
            _searchBox.SelectAll();

            if (!string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                QueueRefresh();
            }
            else
            {
                UpdateStatus();
            }
        }

        public void Hide()
        {
            _flyout?.Hide();
        }

        public void ApplyLocalization()
        {
            if (_searchBox != null) _searchBox.PlaceholderText = Strings.SearchPlaceholder;
            if (_previousButton != null) ToolTipService.SetToolTip(_previousButton, Strings.SearchPreviousTooltip);
            if (_nextButton != null) ToolTipService.SetToolTip(_nextButton, Strings.SearchNextTooltip);
            UpdateStatus();
        }

        public void Dispose()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _navigateLock.Dispose();
            _debounceTimer.Stop();
            _flyout?.Hide();
            _flyout = null;
        }

        private void EnsureFlyout()
        {
            if (_flyout != null) return;

            _searchBox = new TextBox
            {
                Width = 190,
                PlaceholderText = Strings.SearchPlaceholder,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false,
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Left
            };
            _searchBox.TextChanged += (_, _) => QueueRefresh();
            _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;

            _previousButton = CreateIconButton("\uE72B", Strings.SearchPreviousTooltip);
            _previousButton.Click += async (_, _) => await NavigateAsync(-1);

            _nextButton = CreateIconButton("\uE72A", Strings.SearchNextTooltip);
            _nextButton.Click += async (_, _) => await NavigateAsync(1);

            _statusText = new TextBlock
            {
                MaxWidth = 290,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                FontSize = 12,
                Opacity = 0.85,
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Left
            };

            _previewText = new TextBlock
            {
                MaxWidth = 290,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                FontSize = 12,
                Opacity = 0.72,
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Left
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                FlowDirection = FlowDirection.LeftToRight
            };
            row.Children.Add(_searchBox);
            row.Children.Add(_previousButton);
            row.Children.Add(_nextButton);

            var panel = new StackPanel
            {
                Width = 310,
                MaxWidth = 310,
                Spacing = 6,
                Padding = new Thickness(10),
                FlowDirection = FlowDirection.LeftToRight
            };
            panel.Children.Add(row);
            panel.Children.Add(_statusText);
            panel.Children.Add(_previewText);
            panel.PreviewKeyDown += SearchPanel_PreviewKeyDown;

            _flyout = new Flyout
            {
                Content = panel,
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
            _flyout.Closed += (_, _) =>
            {
                _searchCts?.Cancel();
                _queryChanged?.Invoke(null);
                _flyout = null;
            };

            UpdateStatus();
        }

        private static Button CreateIconButton(string glyph, string tooltip)
        {
            var button = new Button
            {
                MinWidth = 34,
                MinHeight = 34,
                Padding = new Thickness(4),
                Content = new FontIcon { Glyph = glyph, FontSize = 14 }
            };
            ToolTipService.SetToolTip(button, tooltip);
            return button;
        }

        private static bool IsVisibleAnchor(FrameworkElement? element)
        {
            return element != null &&
                   element.Visibility == Visibility.Visible &&
                   element.ActualWidth > 0 &&
                   element.ActualHeight > 0;
        }

        private void QueueRefresh()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async Task RefreshMatchesAsync()
        {
            var task = RefreshMatchesCoreAsync();
            _refreshTask = task;
            await task;
            if (ReferenceEquals(_refreshTask, task))
            {
                _refreshTask = null;
            }
        }

        private async Task RefreshMatchesCoreAsync()
        {
            if (_searchBox == null) return;

            string query = _searchBox.Text;
            _searchCts?.Cancel();
            var requestId = ++_searchRequestId;
            var cts = new CancellationTokenSource();
            _searchCts = cts;
            var token = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query))
            {
                _isSearching = false;
                _queryChanged?.Invoke(null);
                _matches.Clear();
                _currentIndex = -1;
                _hasNavigatedCurrentQuery = false;
                if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
                cts.Dispose();
                UpdateStatus();
                return;
            }

            _isSearching = true;
            _matches.Clear();
            _currentIndex = -1;
            _hasNavigatedCurrentQuery = false;
            SetPreview(string.Empty);
            SetStatus(Strings.SearchSearching);
            _queryChanged?.Invoke(query);

            bool keepExplicitStatus = false;
            try
            {
                var matches = await _searchAsync(query, token);
                if (token.IsCancellationRequested || requestId != _searchRequestId) return;

                _matches = matches.OrderBy(m => m.SortKey).ToList();
                _currentIndex = -1;
                _hasNavigatedCurrentQuery = false;
                UpdateStatus();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _matches.Clear();
                    _currentIndex = -1;
                    _hasNavigatedCurrentQuery = false;
                    SetStatus(ex.Message);
                    keepExplicitStatus = true;
                }
            }
            finally
            {
                if (requestId == _searchRequestId)
                {
                    _isSearching = false;
                    if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
                    if (!keepExplicitStatus) UpdateStatus();
                }

                cts.Dispose();
            }
        }

        private async Task NavigateAsync(int direction)
        {
            await _navigateLock.WaitAsync();
            try
            {
                if (_searchBox == null) return;

                if (_debounceTimer.IsRunning)
                {
                    _debounceTimer.Stop();
                    await RefreshMatchesAsync();
                }
                else if (_isSearching && _refreshTask != null)
                {
                    SetStatus(Strings.SearchSearching);
                    await _refreshTask;
                }

                if (_matches.Count == 0)
                {
                    UpdateStatus();
                    return;
                }

                if (!_hasNavigatedCurrentQuery)
                {
                    long currentPosition = _currentPositionProvider();
                    _currentIndex = direction >= 0
                        ? FindLastBefore(currentPosition)
                        : FindFirstAtOrAfter(currentPosition);
                }

                _currentIndex = WrapIndex(_currentIndex + direction, _matches.Count);
                _hasNavigatedCurrentQuery = true;

                var match = _matches[_currentIndex];
                await _navigateAsync(match);
                UpdateStatus();
            }
            finally
            {
                _navigateLock.Release();
                FocusSearchBox();
            }
        }

        private int FindLastBefore(long currentPosition)
        {
            int index = -1;
            for (int i = 0; i < _matches.Count; i++)
            {
                if (_matches[i].SortKey < currentPosition) index = i;
                else break;
            }
            return index;
        }

        private int FindFirstAtOrAfter(long currentPosition)
        {
            for (int i = 0; i < _matches.Count; i++)
            {
                if (_matches[i].SortKey >= currentPosition) return i;
            }
            return _matches.Count;
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0) return -1;
            if (index < 0) return count - 1;
            if (index >= count) return 0;
            return index;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Hide();
                return;
            }

            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                bool shiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                    VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                _ = NavigateAsync(shiftPressed ? -1 : 1);
            }
        }

        private void SearchPanel_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Hide();
            }
        }

        private void FocusSearchBox()
        {
            if (_flyout == null || _searchBox == null) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_flyout == null || _searchBox == null) return;
                _searchBox.Focus(FocusState.Programmatic);
            });
        }

        private void UpdateStatus()
        {
            if (_searchBox == null || string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                SetStatus(string.Empty);
                SetPreview(string.Empty);
                return;
            }

            if (_isSearching)
            {
                SetStatus(Strings.SearchSearching);
                SetPreview(string.Empty);
                return;
            }

            if (_matches.Count == 0)
            {
                SetStatus(Strings.SearchNoMatches);
                SetPreview(string.Empty);
                return;
            }

            int current = _currentIndex >= 0 && _currentIndex < _matches.Count ? _currentIndex + 1 : 0;
            SetStatus(Strings.SearchMatchCounter(current, _matches.Count));
            SetPreview(current > 0 ? _matches[_currentIndex].Preview : string.Empty);
        }

        private void SetStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
            bool enabled = _matches.Count > 0;
            if (_previousButton != null) _previousButton.IsEnabled = enabled;
            if (_nextButton != null) _nextButton.IsEnabled = enabled;
        }

        private void SetPreview(string text)
        {
            if (_previewText != null) _previewText.Text = text;
        }
    }
}
