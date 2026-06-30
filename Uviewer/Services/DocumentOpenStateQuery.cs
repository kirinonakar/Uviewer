using System;
using System.Collections.Generic;
using System.IO;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class DocumentOpenStateQueryHandlers
    {
        public Func<string?> GetCurrentNavigatingPath { get; init; } = null!;
        public Func<string?> GetCurrentPdfPath { get; init; } = null!;
        public Func<string?> GetCurrentArchivePath { get; init; } = null!;
        public Func<string?> GetCurrentEpubPath { get; init; } = null!;
        public Func<string?> GetCurrentTextPath { get; init; } = null!;
        public Func<bool> IsWebDavMode { get; init; } = null!;
        public Func<int> GetCurrentIndex { get; init; } = null!;
        public Func<IReadOnlyList<ImageEntry>> GetImageEntries { get; init; } = null!;
    }

    internal sealed class DocumentOpenStateQuery
    {
        private readonly DocumentOpenStateQueryHandlers _handlers;

        public DocumentOpenStateQuery(DocumentOpenStateQueryHandlers handlers)
        {
            _handlers = handlers;
        }

        public bool IsExplorerOperationTargetOpen(string targetPath, bool targetIsDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return false;

            var currentPath = _handlers.GetCurrentNavigatingPath();
            if (PathsEqual(currentPath, targetPath)) return true;
            if (IsCurrentFile(targetPath)) return true;

            if (PathsEqual(_handlers.GetCurrentPdfPath(), targetPath)) return true;
            if (PathsEqual(_handlers.GetCurrentArchivePath(), targetPath)) return true;
            if (PathsEqual(_handlers.GetCurrentEpubPath(), targetPath)) return true;
            if (PathsEqual(_handlers.GetCurrentTextPath(), targetPath)) return true;

            var entries = _handlers.GetImageEntries();
            var currentIndex = _handlers.GetCurrentIndex();
            if (currentIndex >= 0 && currentIndex < entries.Count)
            {
                var entry = entries[currentIndex];
                if (PathsEqual(entry.FilePath, targetPath) || PathsEqual(entry.WebDavPath, targetPath)) return true;
            }

            if (targetIsDirectory && !string.IsNullOrEmpty(currentPath) && IsSameOrChildPath(currentPath, targetPath))
            {
                return true;
            }

            return false;
        }

        public bool IsCurrentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (StringEquals(_handlers.GetCurrentPdfPath(), path)) return true;
            if (StringEquals(_handlers.GetCurrentArchivePath(), path)) return true;
            if (StringEquals(_handlers.GetCurrentEpubPath(), path)) return true;
            if (StringEquals(_handlers.GetCurrentTextPath(), path)) return true;

            var entries = _handlers.GetImageEntries();
            var currentIndex = _handlers.GetCurrentIndex();

            if (_handlers.IsWebDavMode() && currentIndex >= 0 && currentIndex < entries.Count)
            {
                var entry = entries[currentIndex];
                if (entry.IsWebDavEntry && entry.WebDavPath == path) return true;
                if (entry.IsArchiveEntry && _handlers.GetCurrentArchivePath() != null &&
                    (_handlers.GetCurrentArchivePath() == path || _handlers.GetCurrentArchivePath() == $"WebDAV:{path}")) return true;
            }

            if (currentIndex >= 0 && currentIndex < entries.Count)
            {
                var entry = entries[currentIndex];
                if (entry.IsArchiveEntry)
                {
                    return _handlers.GetCurrentArchivePath() != null &&
                        StringEquals(_handlers.GetCurrentArchivePath(), path);
                }

                return entry.FilePath != null && StringEquals(entry.FilePath, path);
            }

            return false;
        }

        private static bool IsSameOrChildPath(string? candidatePath, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            try
            {
                return Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return StringEquals(first, second);
            }
        }

        private static bool StringEquals(string? first, string? second)
        {
            return !string.IsNullOrEmpty(first) &&
                !string.IsNullOrEmpty(second) &&
                first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }
    }
}
