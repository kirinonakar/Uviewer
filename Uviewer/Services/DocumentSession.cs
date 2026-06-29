using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public enum DocumentKind
    {
        None,
        ImageFile,
        ImageFolder,
        ArchiveImage,
        Pdf,
        Text,
        Epub,
        WebDav
    }

    public readonly record struct NavigationCommand(int Direction)
    {
        public static NavigationCommand Previous { get; } = new(-1);
        public static NavigationCommand Next { get; } = new(1);
    }

    public interface IDocumentSession : IAsyncDisposable
    {
        DocumentKind Kind { get; }
        string DisplayName { get; }
        string? SourcePath { get; }
        Task OpenAsync(CancellationToken token);
        Task NavigateAsync(NavigationCommand command, CancellationToken token);
        Task<IReadOnlyList<DocumentSearchMatch>> SearchAsync(string query, CancellationToken token);
    }

    public abstract class DocumentSessionBase : IDocumentSession
    {
        protected DocumentSessionBase(DocumentKind kind, string displayName, string? sourcePath = null)
        {
            Kind = kind;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Strings.FileSelectPlaceholder : displayName;
            SourcePath = sourcePath;
        }

        public DocumentKind Kind { get; }
        public string DisplayName { get; }
        public string? SourcePath { get; }

        public virtual Task OpenAsync(CancellationToken token) => Task.CompletedTask;

        public virtual Task NavigateAsync(NavigationCommand command, CancellationToken token) => Task.CompletedTask;

        public virtual Task<IReadOnlyList<DocumentSearchMatch>> SearchAsync(string query, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<DocumentSearchMatch>>(Array.Empty<DocumentSearchMatch>());

        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

        protected static string DisplayNameFromPath(string? path, string? displayName = null)
        {
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
            if (string.IsNullOrWhiteSpace(path)) return Strings.FileSelectPlaceholder;

            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }
    }

    public sealed class ImageFileSession : DocumentSessionBase
    {
        public ImageFileSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.ImageFile, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class ImageFolderSession : DocumentSessionBase
    {
        public ImageFolderSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.ImageFolder, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class ArchiveImageSession : DocumentSessionBase
    {
        public ArchiveImageSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.ArchiveImage, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class PdfDocumentSession : DocumentSessionBase
    {
        public PdfDocumentSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.Pdf, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class TextDocumentSession : DocumentSessionBase
    {
        public TextDocumentSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.Text, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class EpubDocumentSession : DocumentSessionBase
    {
        public EpubDocumentSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.Epub, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class WebDavDocumentSession : DocumentSessionBase
    {
        public WebDavDocumentSession(string sourcePath, string? displayName = null)
            : base(DocumentKind.WebDav, DisplayNameFromPath(sourcePath, displayName), sourcePath)
        {
        }
    }

    public sealed class DocumentSessionTracker : IAsyncDisposable
    {
        private IDocumentSession? _current;

        public IDocumentSession? Current => _current;
        public DocumentKind Kind => _current?.Kind ?? DocumentKind.None;
        public string DisplayName => _current?.DisplayName ?? string.Empty;
        public string? SourcePath => _current?.SourcePath;

        public bool Is(DocumentKind kind) => Kind == kind;

        public void Replace(IDocumentSession session)
        {
            _current = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task ReplaceAsync(IDocumentSession session, CancellationToken token = default)
        {
            var previous = _current;
            _current = session ?? throw new ArgumentNullException(nameof(session));

            if (previous != null)
            {
                await previous.DisposeAsync();
            }

            await _current.OpenAsync(token);
        }

        public void Clear(DocumentKind kind)
        {
            if (_current?.Kind == kind)
            {
                _current = null;
            }
        }

        public void Clear()
        {
            _current = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_current != null)
            {
                await _current.DisposeAsync();
                _current = null;
            }
        }
    }
}
