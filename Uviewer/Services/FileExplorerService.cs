using System.Collections.Generic;
using System.IO;
using System.Linq;
using Uviewer.Models;

namespace Uviewer.Services
{
    public static class FileExplorerService
    {
        public static readonly string[] SupportedImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".jxl", ".ico", ".tiff", ".tif"
        };

        public static readonly string[] SupportedTextExtensions =
        {
            ".txt", ".html", ".htm", ".md", ".xml"
        };

        public static readonly string[] SupportedArchiveExtensions =
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".cbz", ".cbr"
        };

        public static readonly string[] SupportedEpubExtensions =
        {
            ".epub"
        };

        public static readonly string[] SupportedPdfExtensions =
        {
            ".pdf"
        };

        public static IEnumerable<string> SupportedFileExtensions =>
            SupportedImageExtensions.Concat(SupportedTextExtensions)
                                    .Concat(SupportedArchiveExtensions)
                                    .Concat(SupportedEpubExtensions)
                                    .Concat(SupportedPdfExtensions);

        public static string? GetEntryExtension(ImageEntry entry)
        {
            if (entry == null) return null;
            if (entry.FilePath != null) return Path.GetExtension(entry.FilePath);
            if (entry.ArchiveEntryKey != null) return Path.GetExtension(entry.ArchiveEntryKey);
            if (entry.WebDavPath != null) return Path.GetExtension(entry.WebDavPath);
            return null;
        }

        public static bool IsTextEntry(ImageEntry entry)
        {
            var ext = GetEntryExtension(entry);
            return !string.IsNullOrEmpty(ext) && SupportedTextExtensions.Contains(ext.ToLowerInvariant());
        }

        public static bool IsEpubEntry(ImageEntry entry)
        {
            var ext = GetEntryExtension(entry);
            return !string.IsNullOrEmpty(ext) && SupportedEpubExtensions.Contains(ext.ToLowerInvariant());
        }

        public static bool IsPdfEntry(ImageEntry entry)
        {
            var ext = GetEntryExtension(entry);
            return (!string.IsNullOrEmpty(ext) && SupportedPdfExtensions.Contains(ext.ToLowerInvariant())) || (entry?.IsPdfEntry ?? false);
        }

        public static bool IsImageEntry(ImageEntry entry)
        {
            var ext = GetEntryExtension(entry);
            return !string.IsNullOrEmpty(ext) && SupportedImageExtensions.Contains(ext.ToLowerInvariant());
        }

        public static bool IsNavigableImage(ImageEntry entry)
        {
            if (entry == null) return false;
            return IsPdfEntry(entry) || IsImageEntry(entry);
        }

        public static string GetFormattedDisplayName(string displayName, bool isArchiveEntry, string? archivePath = null, string? webDavItemPath = null)
        {
            if (isArchiveEntry && !string.IsNullOrEmpty(archivePath))
            {
                if (archivePath.StartsWith("WebDAV:"))
                {
                    archivePath = archivePath.Substring("WebDAV:".Length);
                }
                string archiveName = Path.GetFileName(archivePath);
                return $"{archiveName} - {displayName}";
            }

            if (!string.IsNullOrEmpty(webDavItemPath))
            {
                string realName = Path.GetFileName(webDavItemPath);
                
                if (!string.IsNullOrEmpty(displayName))
                {
                    // If displayName contains " - ", preserve the suffix (e.g., PDF pages)
                    int dashIndex = displayName.IndexOf(" - ");
                    if (dashIndex > 0)
                    {
                        return realName + displayName.Substring(dashIndex);
                    }
                    
                    return realName;
                }
            }

            return displayName;
        }
    }
}
