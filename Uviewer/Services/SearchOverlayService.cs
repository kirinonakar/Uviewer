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
        private List<DocumentSearchMatch> _matches = new();
        private int _currentIndex = -1;
        private bool _hasNavigatedCurrentQuery;

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

        public void Show(FrameworkElement anchor)
        {
            EnsureFlyout();
            if (_flyout == null || _searchBox == null) return;

            _flyout.ShowAt(anchor);
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
                IsTextPredictionEnabled = false
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
                Opacity = 0.85
            };

            _previewText = new TextBlock
            {
                MaxWidth = 290,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                FontSize = 12,
                Opacity = 0.72
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            row.Children.Add(_searchBox);
            row.Children.Add(_previousButton);
            row.Children.Add(_nextButton);

            var panel = new StackPanel
            {
                Width = 310,
                MaxWidth = 310,
                Spacing = 6,
                Padding = new Thickness(10)
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

        private void QueueRefresh()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async Task RefreshMatchesAsync()
        {
            if (_searchBox == null) return;

            string query = _searchBox.Text;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query))
            {
                _queryChanged?.Invoke(null);
                _matches.Clear();
                _currentIndex = -1;
                _hasNavigatedCurrentQuery = false;
                UpdateStatus();
                return;
            }

            SetStatus(Strings.SearchSearching);
            _queryChanged?.Invoke(query);

            try
            {
                var matches = await _searchAsync(query, token);
                if (token.IsCancellationRequested) return;

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
                }
            }
        }

        private async Task NavigateAsync(int direction)
        {
            try
            {
                if (_searchBox == null) return;

                if (_debounceTimer.IsRunning)
                {
                    _debounceTimer.Stop();
                    await RefreshMatchesAsync();
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
