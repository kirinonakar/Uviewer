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
                ShowNotification("필수 입력값을 확인해주세요");
                return;
            }

            _webDavService.SaveServer(result.ServerInfo);
            ShowNotification($"서버 '{result.ServerInfo.ServerName}' 저장됨");
        }

        /// <summary>
        /// WebDAV Flyout의 서버 목록 UI 갱신
        /// </summary>
        private void UpdateWebDavServerList()
        {
            if (WebDavPanel == null) return;

            _webDavServerUiService.RefreshServerList(
                WebDavPanel,
                _webDavService.GetSavedServerNames(),
                _settingsManager?.UIFontFamily,
                WebDavServerItem_Click,
                WebDavDeleteServer_Click);
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
            if (sender is Button btn && btn.Tag is string serverName)
            {
                _webDavService.DeleteServer(serverName);
                ShowNotification($"서버 '{serverName}' 삭제됨");
                UpdateWebDavServerList();
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
                if (!connected)
                {
                    FileNameText.Text = Strings.WebDavConnectionFailed;
                    ShowNotification(Strings.WebDavConnectionFailed);
                    return;
                }

                _isWebDavMode = true;
                _currentWebDavPath = "/";

                ShowNotification($"'{serverName}' 연결됨");
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
        private async Task LoadWebDavFolderAsync(string remotePath)
        {
            if (!_webDavService.IsConnected || _webDavService.CurrentServer == null)
                return;

            var token = _webDavState.RestartOperation();

            try
            {
                _currentWebDavPath = remotePath;
                _currentExplorerPath = null; // 로컬 경로 초기화

                CurrentPathText.Text = $"WebDAV: {_webDavService.CurrentServer.ServerName}{remotePath}";

                _imageViewerController.ClearImageResources();
                _explorerState.ReplaceItems(System.Array.Empty<FileItem>());
                _imageEntries.Clear();
                _currentIndex = -1;

                var parentItem = WebDavExplorerItemFactory.CreateParentItem(remotePath);
                if (parentItem != null)
                {
                    _fileItems.Add(parentItem);
                }

                var items = await _webDavService.ListFolderAsync(remotePath, token);
                if (token.IsCancellationRequested) return;

                if (items.Count == 0 && remotePath != "/")
                {
                    ShowNotification(Strings.FolderEmpty);
                }
                else if (items.Count == 0 && remotePath == "/")
                {
                    FileNameText.Text = "WebDAV: 원격 서버에 파일이 없거나 경로가 잘못되었습니다.";
                }

                var explorerItems = WebDavExplorerItemFactory.CreateFolderItems(remotePath, items, _explorerSortMode);
                _explorerState.ReplaceItems(explorerItems);
                _explorerSidebarController.ApplyThumbnailSizeToFileItems();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CurrentPathText.Text = $"WebDAV 오류: {ex.Message}";
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
        private Task OpenWebDavFileAsync(FileItem item) =>
            _webDavDocumentOpenCoordinator.OpenDownloadedFileAsync(item);

        /// <summary>
        /// WebDAV 압축 파일 열기 - 스트리밍 다운로드 → MemoryStream → SharpCompress
        /// </summary>
        private Task OpenWebDavArchiveAsync(FileItem item) =>
            _webDavDocumentOpenCoordinator.OpenStreamedArchiveAsync(item);

        private ImageEntry? PrepareWebDavSequentialEntries(string webDavPath, string tempPath)
        {
            var viewableItems = _fileItems
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

        private async Task OpenWebDavArchiveStreamAsync(string webDavPath, Stream stream)
        {
            // SharpCompress needs the stream to stay alive, so do not dispose it here.
            _imageEntries = (await _archiveSession.OpenStreamAsync($"WebDAV:{webDavPath}", stream)).ToList();

            if (_imageEntries.Count > 0)
            {
                _currentIndex = 0;
                await _imageViewerController.DisplayCurrentImageAsync();
                StartWebDavPreload();
            }
            else
            {
                FileNameText.Text = "이 압축 파일에 이미지가 없습니다";
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
