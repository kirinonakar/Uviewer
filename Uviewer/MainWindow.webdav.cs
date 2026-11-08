using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private readonly WebDavService _webDavService = new();
        private readonly WebDavState _webDavState = new();
        private readonly WebDavServerUiService _webDavServerUiService = new();
        private string? _currentWebDavPath
        {
            get => _webDavState.CurrentPath;
            set => _webDavState.CurrentPath = value;
        }
        private string? _currentWebDavItemPath
        {
            get => _webDavState.CurrentItemPath;
            set => _webDavState.CurrentItemPath = value;
        }
        private bool _isWebDavMode
        {
            get => _webDavState.IsWebDavMode;
            set => _webDavState.IsWebDavMode = value;
        }

        #region WebDAV UI

        /// <summary>
        /// WebDAV Flyout이 열린 후 서버 목록 갱신
        /// </summary>
        private void WebDavFlyout_Opened(object? sender, object e)
        {
            UpdateWebDavServerList();
        }

        /// <summary>
        /// "➕ webdav 서버 추가" 버튼 클릭
        /// </summary>
        private async void AddWebDavButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebDavFlyout.Hide();
                await ShowAddWebDavServerDialogAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddWebDavButton_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        /// <summary>
        /// 서버 추가 다이얼로그 표시
        /// </summary>
        private async Task ShowAddWebDavServerDialogAsync()
        {
            var result = await _webDavServerUiService.ShowAddServerDialogAsync(
                RootGrid.XamlRoot,
                RootGrid.ActualTheme);

            if (!result.WasSubmitted)
            {
                return;
            }

            if (!result.IsValid || result.ServerInfo == null)
            {
                ShowNotification(Strings.RequiredFieldsMissing);
                return;
            }

            _webDavService.SaveServer(result.ServerInfo);
            ShowNotification(Strings.WebDavServerSaved(result.ServerInfo.ServerName));
        }

        /// <summary>
        /// WebDAV Flyout의 서버 목록 UI 갱신
        /// </summary>
        private void UpdateWebDavServerList()
        {
            try
            {
                if (WebDavPanel == null) return;

                _webDavServerUiService.RefreshServerList(
                    WebDavPanel,
                    _webDavService.GetSavedServerNames(),
                    _settingsManager?.UIFontFamily,
                    WebDavServerItem_Click,
                    WebDavDeleteServer_Click);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                ShowNotification(ex.Message, "\uE783", "Red");
            }
        }

        /// <summary>
        /// 저장된 서버 항목 클릭 → 접속
        /// </summary>
        private async void WebDavServerItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string serverName)
                {
                    WebDavFlyout.Hide();
                    await ConnectToWebDavServerAsync(serverName, true);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in WebDavServerItem_Click: {ex.Message}");
                ShowNotification($"{ex.Message}", "\uE783", "Red");
            }
        }

        /// <summary>
        /// 서버 삭제 버튼 클릭
        /// </summary>
        private void WebDavDeleteServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string serverName)
                {
                    _webDavService.DeleteServer(serverName);
                    ShowNotification(Strings.WebDavServerDeleted(serverName));
                    UpdateWebDavServerList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                ShowNotification(ex.Message, "\uE783", "Red");
            }
        }

        #endregion

        #region WebDAV Connection & Navigation

        /// <summary>
        /// WebDAV 서버 접속 및 루트 폴더 로드
        /// </summary>
        private async Task ConnectToWebDavServerAsync(string serverName, bool loadRoot = true)
        {
            var serverInfo = _webDavService.LoadServer(serverName);
            if (serverInfo == null)
            {
                ShowNotification(Strings.WebDavConnectionFailed);
                return;
            }

            FileNameText.Text = Strings.WebDavConnecting;

            var token = _webDavState.RestartOperation();

            try
            {
                var connected = await _webDavService.ConnectAsync(serverInfo, token);
                while (!connected && _webDavService.CertificateError is { } certificateError)
                {
                    var proceed = await _webDavServerUiService.ShowCertificateWarningAsync(
                        RootGrid.XamlRoot, RootGrid.ActualTheme, serverName, certificateError, token);
                    token.ThrowIfCancellationRequested();
                    if (!proceed)
                    {
                        DisconnectWebDav();
                        FileNameText.Text = Strings.WebDavConnectionCancelled;
                        return;
                    }

                    connected = await _webDavService.ConnectAsync(serverInfo, token, certificateError);
                }
                if (!connected)
                {
                    FileNameText.Text = Strings.WebDavConnectionFailed;
                    ShowNotification(Strings.WebDavConnectionFailed);
                    return;
                }

                _isWebDavMode = true;
                _currentWebDavPath = "/";

                ShowNotification(Strings.WebDavServerConnected(serverName));
                if (loadRoot)
                {
                    await LoadWebDavFolderAsync("/");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileNameText.Text = $"{Strings.WebDavConnectionFailed}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"WebDAV connect error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebDAV 원격 폴더를 탐색기에 로드
        /// </summary>
        private async Task LoadWebDavFolderAsync(string remotePath, bool openFirstFile = false)
        {
            if (!_webDavService.IsConnected || _webDavService.CurrentServer == null)
                return;

            _explorerController.CancelFilterSearch();
            var token = _webDavState.RestartOperation();

            try
            {
                _currentWebDavPath = remotePath;
                _currentExplorerPath = null; // 로컬 경로 초기화

                _explorerSidebarController.RecordNavigation(isWebDav: true, remotePath);

                CurrentPathBreadcrumb.SetWebDavPath(_webDavService.CurrentServer.ServerName, remotePath);

                _imageViewerController.ClearImageResources();
                _imageEntries.Clear();
                _currentIndex = -1;

                var parentItem = WebDavExplorerItemFactory.CreateParentItem(remotePath);
                _explorerState.ReplaceItems(parentItem != null
                    ? new[] { parentItem }
                    : System.Array.Empty<FileItem>());

                var items = await _webDavService.ListFolderAsync(remotePath, token);
                if (token.IsCancellationRequested) return;

                if (items.Count == 0 && remotePath != "/")
                {
                    ShowNotification(Strings.FolderEmpty);
                }
                else if (items.Count == 0 && remotePath == "/")
                {
                    FileNameText.Text = Strings.WebDavRemotePathEmpty;
                }

                var explorerItems = WebDavExplorerItemFactory.CreateFolderItems(remotePath, items, _explorerSortMode);
                _explorerState.ReplaceItems(explorerItems);
                _explorerSidebarController.ApplyThumbnailSizeToFileItems();
                _explorerController.RefreshFilterSearch();

                if (openFirstFile)
                {
                    var files = explorerItems
                        .Where(item => !item.IsDirectory && !item.IsParentDirectory)
                        .OrderBy(item => item.Name, NaturalSortComparer.Default)
                        .ToList();
                    var firstFile = files.FirstOrDefault(item => item.IsImage);
                    if (firstFile != null)
                    {
                        await OpenWebDavFileAsync(firstFile);
                        return;
                    }

                    foreach (var archive in files.Where(item => item.IsArchive))
                    {
                        if (await _webDavDocumentOpenCoordinator.TryOpenImageArchiveAsync(archive, token))
                        {
                            return;
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    var firstText = files.FirstOrDefault(item => item.IsText);
                    if (firstText != null)
                    {
                        await OpenWebDavFileAsync(firstText);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CurrentPathBreadcrumb.Text = Strings.WebDavError(ex.Message);
                System.Diagnostics.Debug.WriteLine($"WebDAV load folder error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebDAV 파일 항목 클릭 처리 (폴더/파일)
        /// </summary>
        private async Task HandleWebDavFileSelectionAsync(FileItem item)
        {
            await _webDavDocumentOpenCoordinator.OpenItemAsync(item);
        }

        /// <summary>
        /// WebDAV 일반 파일 (이미지/텍스트/epub) 열기 - 임시 파일로 다운로드 후 표시
        /// </summary>
        private Task OpenWebDavFileAsync(FileItem item, string? initialEntryKey = null) =>
            _webDavDocumentOpenCoordinator.OpenDownloadedFileAsync(item, initialEntryKey);

        /// <summary>
        /// WebDAV 압축 파일 열기 - 스트리밍 다운로드 → MemoryStream → SharpCompress
        /// </summary>
        private Task OpenWebDavArchiveAsync(FileItem item, string? initialEntryKey = null) =>
            _webDavDocumentOpenCoordinator.OpenStreamedArchiveAsync(item, initialEntryKey);

        private ImageEntry? PrepareWebDavSequentialEntries(string webDavPath, string tempPath)
        {
            // Reader navigation is independent of the sidebar filter, including
            // files opened from favorites or recent items while hidden by it.
            var viewableItems = _explorerState.AllItems
                .Where(f => !f.IsDirectory && !f.IsParentDirectory)
                .ToList();

            _imageEntries = viewableItems
                .Select(f => new ImageEntry
                {
                    DisplayName = f.Name,
                    WebDavPath = f.WebDavPath
                })
                .ToList();

            _currentIndex = _imageEntries.FindIndex(e => e.WebDavPath == webDavPath);
            if (_currentIndex >= 0)
            {
                _imageEntries[_currentIndex].FilePath = tempPath;
                return _imageEntries[_currentIndex];
            }

            return null;
        }

        private async Task OpenWebDavArchiveStreamAsync(string webDavPath, Stream stream, string? initialEntryKey)
        {
            // SharpCompress needs the stream to stay alive, so do not dispose it here.
            _imageEntries = (await _archiveSession.OpenStreamAsync($"WebDAV:{webDavPath}", stream)).ToList();

            if (_imageEntries.Count > 0)
            {
                _currentIndex = ArchiveOpenPosition.Resolve(_imageEntries, initialEntryKey);
                await _imageViewerController.DisplayCurrentImageAsync();
                StartWebDavPreload();
            }
            else
            {
                FileNameText.Text = Strings.ArchiveNoImages;
            }
        }

        private void StartWebDavPreload()
        {
            _imageViewerController.StartImagePreload(prioritizeNext: true);
        }

        /// <summary>
        /// WebDAV 모드 비활성화
        /// </summary>
        private void DisconnectWebDav()
        {
            _webDavService.Disconnect();
            _isWebDavMode = false;
            _explorerController.CancelFilterSearch();
            _currentWebDavPath = null;
        }

        #endregion
        private string? ResolveWebDavImagePath(string relativePath)
        {
            if (string.IsNullOrEmpty(_currentWebDavItemPath)) return null;

            // Normalize
            string rel = relativePath.Replace('\\', '/').TrimStart('/');
            string baseDir = "";

            int lastSlash = _currentWebDavItemPath.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                baseDir = _currentWebDavItemPath.Substring(0, lastSlash + 1);
            }
            else
            {
                baseDir = "/";
            }

            return baseDir + rel;
        }
    }
}
