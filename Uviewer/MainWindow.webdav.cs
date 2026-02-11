using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private readonly WebDavService _webDavService = new();
        private string? _currentWebDavPath;
        private string? _currentWebDavItemPath; // 현재 열려있는 WebDAV 파일의 전체 경로 (네비게이션용)
        private bool _isWebDavMode = false;
        private CancellationTokenSource? _webDavCts;

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
            WebDavFlyout.Hide();
            await ShowAddWebDavServerDialogAsync();
        }

        /// <summary>
        /// 서버 추가 다이얼로그 표시
        /// </summary>
        private async Task ShowAddWebDavServerDialogAsync()
        {
            var nameBox = new TextBox
            {
                PlaceholderText = Strings.WebDavServerName,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var addressBox = new TextBox
            {
                PlaceholderText = Strings.WebDavAddress,
                Margin = new Thickness(0, 0, 0, 8),
                CharacterCasing = CharacterCasing.Normal,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false
            };
            var portBox = new NumberBox
            {
                PlaceholderText = Strings.WebDavPort,
                Value = 443,
                Minimum = 1,
                Maximum = 65535,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var idBox = new TextBox
            {
                PlaceholderText = Strings.WebDavId,
                Margin = new Thickness(0, 0, 0, 8),
                CharacterCasing = CharacterCasing.Normal,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false
            };
            var passwordBox = new PasswordBox
            {
                PlaceholderText = Strings.WebDavPassword,
                Margin = new Thickness(0, 0, 0, 0)
            };

            var panel = new StackPanel { Width = 320 };
            panel.Children.Add(new TextBlock
            {
                Text = Strings.WebDavServerName,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock
            {
                Text = Strings.WebDavAddress,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(addressBox);
            panel.Children.Add(new TextBlock
            {
                Text = Strings.WebDavPort,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(portBox);
            panel.Children.Add(new TextBlock
            {
                Text = Strings.WebDavId,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(idBox);
            panel.Children.Add(new TextBlock
            {
                Text = Strings.WebDavPassword,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(passwordBox);

            var dialog = new ContentDialog
            {
                Title = Strings.AddWebDavServer,
                Content = panel,
                PrimaryButtonText = Strings.WebDavSave,
                CloseButtonText = Strings.WebDavCancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var serverName = nameBox.Text.Trim();
                var address = addressBox.Text.Trim();
                var port = (int)portBox.Value;
                var userId = idBox.Text.Trim();
                var password = passwordBox.Password;

                if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(address) || string.IsNullOrEmpty(userId))
                {
                    ShowNotification("필수 입력값을 확인해주세요");
                    return;
                }

                var serverInfo = new WebDavServerInfo
                {
                    ServerName = serverName,
                    Address = address,
                    Port = port,
                    UserId = userId,
                    Password = password
                };

                _webDavService.SaveServer(serverInfo);
                ShowNotification($"서버 '{serverName}' 저장됨");
            }
        }

        /// <summary>
        /// WebDAV Flyout의 서버 목록 UI 갱신
        /// </summary>
        private void UpdateWebDavServerList()
        {
            if (WebDavPanel == null) return;

            // 기존 동적 항목 제거 (첫 2개: 추가 버튼 + 구분선)
            while (WebDavPanel.Children.Count > 2)
            {
                WebDavPanel.Children.RemoveAt(2);
            }

            var serverNames = _webDavService.GetSavedServerNames();

            foreach (var name in serverNames)
            {
                var itemGrid = new Grid
                {
                    Margin = new Thickness(0, 2, 0, 2)
                };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 서버 이름 버튼 (클릭하면 연결)
                var serverButton = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE774", FontSize = 14, Foreground = new SolidColorBrush(Colors.CornflowerBlue) },
                            new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }
                        }
                    },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Tag = name
                };
                serverButton.Click += WebDavServerItem_Click;
                Grid.SetColumn(serverButton, 0);

                // X 삭제 버튼
                var deleteButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = name
                };
                deleteButton.Click += WebDavDeleteServer_Click;
                Grid.SetColumn(deleteButton, 1);

                itemGrid.Children.Add(serverButton);
                itemGrid.Children.Add(deleteButton);

                WebDavPanel.Children.Add(itemGrid);
            }
        }

        /// <summary>
        /// 저장된 서버 항목 클릭 → 접속
        /// </summary>
        private async void WebDavServerItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string serverName)
            {
                WebDavFlyout.Hide();
                await ConnectToWebDavServerAsync(serverName);
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
        private async Task ConnectToWebDavServerAsync(string serverName)
        {
            var serverInfo = _webDavService.LoadServer(serverName);
            if (serverInfo == null)
            {
                ShowNotification(Strings.WebDavConnectionFailed);
                return;
            }

            FileNameText.Text = Strings.WebDavConnecting;

            _webDavCts?.Cancel();
            _webDavCts = new CancellationTokenSource();

            try
            {
                var connected = await _webDavService.ConnectAsync(serverInfo, _webDavCts.Token);
                if (!connected)
                {
                    FileNameText.Text = Strings.WebDavConnectionFailed;
                    ShowNotification(Strings.WebDavConnectionFailed);
                    return;
                }

                _isWebDavMode = true;
                _currentWebDavPath = "/";

                ShowNotification($"'{serverName}' 연결됨");
                await LoadWebDavFolderAsync("/");
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

            _webDavCts?.Cancel();
            _webDavCts = new CancellationTokenSource();
            var token = _webDavCts.Token;

            try
            {
                _currentWebDavPath = remotePath;
                _currentExplorerPath = null; // 로컬 경로 초기화

                CurrentPathText.Text = $"WebDAV: {_webDavService.CurrentServer.ServerName}{remotePath}";

                _fileItems.Clear();

                // 상위 폴더 항목
                if (remotePath != "/")
                {
                    var parentPath = remotePath.TrimEnd('/');
                    var lastSlash = parentPath.LastIndexOf('/');
                    var parent = lastSlash > 0 ? parentPath.Substring(0, lastSlash + 1) : "/";

                    _fileItems.Add(new FileItem
                    {
                        Name = "..",
                        FullPath = parent,
                        IsDirectory = true,
                        IsParentDirectory = true,
                        IsWebDav = true,
                        WebDavPath = parent
                    });
                }

                var items = await _webDavService.ListFolderAsync(remotePath, token);
                if (token.IsCancellationRequested) return;

                foreach (var item in items)
                {
                    var name = item.Name;
                    var ext = Path.GetExtension(name).ToLowerInvariant();

                    var isImage = SupportedImageExtensions.Contains(ext);
                    var isArchive = SupportedArchiveExtensions.Contains(ext);
                    var isText = SupportedTextExtensions.Contains(ext);
                    var isEpub = SupportedEpubExtensions.Contains(ext);

                    if (item.IsDirectory || isImage || isArchive || isText || isEpub)
                    {
                        _fileItems.Add(new FileItem
                        {
                            Name = name,
                            FullPath = item.FullPath,
                            IsDirectory = item.IsDirectory,
                            IsImage = isImage,
                            IsArchive = isArchive,
                            IsText = isText,
                            IsEpub = isEpub,
                            IsWebDav = true,
                            WebDavPath = item.FullPath
                        });
                    }
                }
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
            if (!item.IsWebDav || string.IsNullOrEmpty(item.WebDavPath))
                return;

            if (item.IsDirectory)
            {
                await LoadWebDavFolderAsync(item.WebDavPath);
            }
            else if (item.IsArchive)
            {
                await OpenWebDavArchiveAsync(item);
            }
            else if (item.IsImage || item.IsText || item.IsEpub)
            {
                await OpenWebDavFileAsync(item);
            }
        }

        /// <summary>
        /// WebDAV 일반 파일 (이미지/텍스트/epub) 열기 - 임시 파일로 다운로드 후 표시
        /// </summary>
        private async Task OpenWebDavFileAsync(FileItem item)
        {
            if (string.IsNullOrEmpty(item.WebDavPath)) return;

            _currentWebDavItemPath = item.WebDavPath;
            FileNameText.Text = $"다운로드 중: {item.Name}...";

            _webDavCts?.Cancel();
            _webDavCts = new CancellationTokenSource();

            try
            {
                var tempPath = await _webDavService.DownloadToTempFileAsync(item.WebDavPath, _webDavCts.Token);
                if (string.IsNullOrEmpty(tempPath))
                {
                    FileNameText.Text = "다운로드 실패";
                    return;
                }

                var ext = Path.GetExtension(item.Name).ToLowerInvariant();

                if (SupportedArchiveExtensions.Contains(ext))
                {
                    await LoadImagesFromArchiveAsync(tempPath);
                }
                else
                {
                    var file = await StorageFile.GetFileFromPathAsync(tempPath);
                    await LoadImageFromFileAsync(file);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileNameText.Text = $"파일 열기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"WebDAV open file error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebDAV 압축 파일 열기 - 스트리밍 다운로드 → MemoryStream → SharpCompress
        /// </summary>
        private async Task OpenWebDavArchiveAsync(FileItem item)
        {
            if (string.IsNullOrEmpty(item.WebDavPath)) return;

            _currentWebDavItemPath = item.WebDavPath;
            FileNameText.Text = $"스트리밍 다운로드 중: {item.Name}...";

            _webDavCts?.Cancel();
            _webDavCts = new CancellationTokenSource();

            try
            {
                // Do NOT use 'using' - SharpCompress needs the stream to stay alive
                var stream = await _webDavService.DownloadFileAsync(item.WebDavPath, _webDavCts.Token);
                if (stream == null)
                {
                    FileNameText.Text = "다운로드 실패";
                    return;
                }

                // SharpCompress needs seekable stream - we already have MemoryStream from service
                await _archiveLock.WaitAsync();
                try
                {
                    CloseCurrentArchiveInternal();

                    _currentArchivePath = $"WebDAV:{item.WebDavPath}";
                    _currentArchive = ArchiveFactory.Open(stream);

                    _imageEntries = _currentArchive.Entries
                        .Where(e => !e.IsDirectory &&
                            SupportedImageExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant()))
                        .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(e => new ImageEntry
                        {
                            DisplayName = Path.GetFileName(e.Key ?? "Unknown"),
                            ArchiveEntryKey = e.Key
                        })
                        .ToList();
                }
                finally
                {
                    _archiveLock.Release();
                }

                if (_imageEntries.Count > 0)
                {
                    _currentIndex = 0;
                    await DisplayCurrentImageAsync();

                    _preloadCts?.Cancel();
                    _preloadCts?.Dispose();
                    _preloadCts = new CancellationTokenSource();
                    var token = _preloadCts.Token;
                    _ = Task.Run(() => PreloadNextImagesAsync(token));
                }
                else
                {
                    FileNameText.Text = "이 압축 파일에 이미지가 없습니다";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileNameText.Text = $"압축 파일 열기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"WebDAV archive error: {ex.Message}");
            }
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
    }
}
