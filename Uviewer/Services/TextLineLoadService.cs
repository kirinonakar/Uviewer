using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public sealed class TextLineLoadPlan
    {
        private readonly string[] _lines;

        public TextLineLoadPlan(string[] lines, int initialLimit)
        {
            _lines = lines;
            InitialLimit = initialLimit;
        }

        public int TotalLineCount => _lines.Length;
        public int InitialLimit { get; }
        public bool RequiresProgressiveLoad => _lines.Length > InitialLimit;

        public string[] GetInitialLines()
        {
            int count = Math.Min(_lines.Length, InitialLimit);
            var result = new string[count];
            Array.Copy(_lines, result, count);
            return result;
        }

        public string[] GetAllLines()
        {
            var result = new string[_lines.Length];
            Array.Copy(_lines, result, _lines.Length);
            return result;
        }

        public string[] GetRemainingLines()
        {
            if (!RequiresProgressiveLoad)
            {
                return Array.Empty<string>();
            }

            int count = _lines.Length - InitialLimit;
            var result = new string[count];
            Array.Copy(_lines, InitialLimit, result, 0, count);
            return result;
        }
    }

    public sealed class TextLineLoadService
    {
        private const int BaseInitialLimit = 2000;
        private const int TargetLineLookahead = 500;

        private readonly TextLineLayoutService _layoutService;

        public TextLineLoadService(TextLineLayoutService layoutService)
        {
            _layoutService = layoutService;
        }

        public TextLineLoadPlan CreatePlan(string content, int targetLine)
        {
            var lines = TextLineLayoutService.SplitNormalizedLines(content);
            int initialLimit = CalculateInitialLimit(targetLine);
            return new TextLineLoadPlan(lines, initialLimit);
        }

        public Task<List<TextLine>> CreateInitialLinesAsync(
            TextLineLoadPlan plan,
            TextLineStyle style,
            CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return _layoutService.CreatePlainLines(plan.GetInitialLines(), style);
            }, token);
        }

        public Task<List<TextLine>> CreateAllLinesAsync(
            TextLineLoadPlan plan,
            TextLineStyle style,
            CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return _layoutService.CreatePlainLines(plan.GetAllLines(), style);
            }, token);
        }

        public List<TextLine> CreateRemainingLines(
            TextLineLoadPlan plan,
            TextLineStyle style,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var remainingLines = plan.GetRemainingLines();
            token.ThrowIfCancellationRequested();
            return _layoutService.CreatePlainLines(remainingLines, style);
        }

        private static int CalculateInitialLimit(int targetLine)
        {
            int initialLimit = BaseInitialLimit;
            if (targetLine > initialLimit - TargetLineLookahead)
            {
                initialLimit = targetLine + TargetLineLookahead;
            }

            return initialLimit;
        }
    }
}
