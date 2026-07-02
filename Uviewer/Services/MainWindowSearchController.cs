using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal sealed class MainWindowSearchController
    {
        private readonly DocumentSearchCoordinatorService _coordinator;
        private readonly Func<DocumentSearchCoordinatorContext> _createContext;

        public MainWindowSearchController(
            DocumentSearchCoordinatorService coordinator,
            Func<DocumentSearchCoordinatorContext> createContext)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
        }

        public string? ActiveSearchQuery => _createContext().State.Query;

        public bool CanSearchCurrentDocument => _coordinator.CanSearch(_createContext());

        public void HandleRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowOverlay(sender as FrameworkElement);
        }

        public void ShowOverlay(FrameworkElement? anchor = null) =>
            _coordinator.ShowOverlay(_createContext(), anchor);

        public void SetActiveQuery(string? query) =>
            _coordinator.SetActiveQuery(_createContext(), query);

        public Task RefreshPdfHighlightsAsync(int pageIndex, int currentMatchIndex = -1) =>
            _coordinator.RefreshPdfHighlightsAsync(_createContext(), pageIndex, currentMatchIndex);

        public void InvalidateHighlights() =>
            _coordinator.InvalidateHighlights(_createContext());

        public void ApplyHighlightsToTextBlock(TextBlock textBlock, string content, int lineNumber) =>
            _coordinator.ApplyHighlightsToTextBlock(_createContext(), textBlock, content, lineNumber);

        public DocumentSearchMatch? GetActiveMatchFor(DocumentSearchKind kind) =>
            _coordinator.GetActiveMatchFor(_createContext(), kind);

        public Task<IReadOnlyList<DocumentSearchMatch>> SearchCurrentDocumentAsync(
            string query,
            CancellationToken token) =>
            _coordinator.SearchCurrentDocumentAsync(_createContext(), query, token);

        public long GetCurrentPosition() =>
            _coordinator.GetCurrentPosition(_createContext());

        public Task NavigateToMatchAsync(DocumentSearchMatch match) =>
            _coordinator.NavigateToMatchAsync(_createContext(), match);
    }
}
