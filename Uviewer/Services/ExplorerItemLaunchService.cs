using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed class ExplorerItemLaunchService
    {
        public async Task OpenWithExternalProgramAsync(
            FileItem? item,
            string configuredExternalProgramPath,
            Func<Task> selectExternalProgramAsync,
            Action refreshExplorer,
            Action<string, string, string> showNotification)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            var externalProgramPath = NormalizeExternalProgramPath(configuredExternalProgramPath);
            if (string.IsNullOrWhiteSpace(externalProgramPath))
            {
                showNotification(Strings.ExternalProgramPathRequired, "\uE783", "Red");
                await selectExternalProgramAsync();
                return;
            }

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                showNotification(Strings.FileNotFound, "\uE7BA", "Red");
                refreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = externalProgramPath,
                    Arguments = QuoteArgument(item.FullPath),
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                showNotification(Strings.ExternalProgramLaunchFailed(ex.Message), "\uE783", "Red");
            }
        }

        public void OpenWithDefaultProgram(
            FileItem? item,
            Action refreshExplorer,
            Action<string, string, string> showNotification)
        {
            if (item == null || item.IsParentDirectory || item.IsWebDav) return;

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                showNotification(Strings.FileNotFound, "\uE7BA", "Red");
                refreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                showNotification(Strings.DefaultProgramLaunchFailed(ex.Message), "\uE783", "Red");
            }
        }

        public void OpenInWindowsExplorer(
            FileItem? item,
            Action refreshExplorer,
            Action<string, string, string> showNotification)
        {
            if (item == null || item.IsWebDav) return;

            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath))
            {
                showNotification(Strings.FileNotFound, "\uE7BA", "Red");
                refreshExplorer();
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = item.IsDirectory ? QuoteArgument(item.FullPath) : $"/select,{QuoteArgument(item.FullPath)}",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                showNotification(Strings.ExplorerOpenFailed(ex.Message), "\uE783", "Red");
            }
        }

        public static string NormalizeExternalProgramPath(string? path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                return value[1..^1].Trim();
            }

            return value;
        }

        public static string GetExternalProgramDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }

        private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
