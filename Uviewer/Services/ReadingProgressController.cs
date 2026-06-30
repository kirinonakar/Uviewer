using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal enum RecentSavePolicy
    {
        None,
        Always,
        WhenLineChanges
    }

    internal sealed class ReadingProgressController
    {
        private readonly TextStatusBarService _statusBarService;
        private readonly ITextReaderViewHost _viewHost;
        private readonly Func<Task> _saveCurrentPositionAsync;

        public ReadingProgressController(
            TextStatusBarService statusBarService,
            ITextReaderViewHost viewHost,
            Func<Task> saveCurrentPositionAsync)
        {
            _statusBarService = statusBarService ?? throw new ArgumentNullException(nameof(statusBarService));
            _viewHost = viewHost ?? throw new ArgumentNullException(nameof(viewHost));
            _saveCurrentPositionAsync = saveCurrentPositionAsync ?? throw new ArgumentNullException(nameof(saveCurrentPositionAsync));
        }

        public void UpdatePlainText(
            string? fileName,
            bool isArchiveEntry,
            int? totalLines,
            TextReaderState state,
            ScrollViewer scrollViewer,
            int currentLine)
        {
            var content = _statusBarService.Create(
                fileName,
                isArchiveEntry,
                totalLines,
                state,
                scrollViewer,
                currentLine);

            Apply(content, state, RecentSavePolicy.WhenLineChanges);
        }

        public void UpdatePagedReader(
            ReaderPageState pageState,
            int currentLine,
            int totalLines,
            TextReaderState state,
            RecentSavePolicy recentSavePolicy)
        {
            var content = _statusBarService.CreatePagedReader(
                pageState,
                currentLine,
                totalLines);

            Apply(content, state, recentSavePolicy);
        }

        private void Apply(
            TextStatusBarContent content,
            TextReaderState state,
            RecentSavePolicy recentSavePolicy)
        {
            if (content.FileName != null)
            {
                _viewHost.FileNameText.Text = content.FileName;
            }

            _viewHost.ImageInfoText.Text = content.LineInfo;
            _viewHost.TextProgressText.Text = content.ProgressText;
            _viewHost.ImageIndexText.Text = content.PageInfo;

            if (ShouldSaveRecent(state, content.CurrentLine, recentSavePolicy))
            {
                state.LastRecentSaveLine = content.CurrentLine;
                _ = _saveCurrentPositionAsync();
            }
        }

        private static bool ShouldSaveRecent(
            TextReaderState state,
            int currentLine,
            RecentSavePolicy recentSavePolicy)
        {
            return recentSavePolicy switch
            {
                RecentSavePolicy.Always => true,
                RecentSavePolicy.WhenLineChanges => currentLine != state.LastRecentSaveLine,
                _ => false
            };
        }
    }
}
