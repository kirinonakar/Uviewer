using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uviewer.Models;
using Uviewer.Services;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Uviewer
{
    public sealed partial class MainWindow : Window
    {
        private readonly WebDavService _webDavService = new();
        private readonly WebDavState _webDavState = new();
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
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
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
                var nameTb = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(_settingsManager.UIFontFamily) && _settingsManager.UIFontFamily != "Unknown")
                {
                    try { nameTb.FontFamily = new FontFamily(_settingsManager.UIFontFamily); }
                    catch { }
                }

                var serverButton = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE774", FontSize = 14, Foreground = new SolidColorBrush(Colors.CornflowerBlue) },
                            nameTb
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

                ClearImageResources();
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
                ApplyThumbnailSizeToFileItems();
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
                await DisplayCurrentImageAsync();
                StartWebDavPreload();
            }
            else
            {
                FileNameText.Text = "이 압축 파일에 이미지가 없습니다";
            }
        }

        private void StartWebDavPreload()
        {
            StartImagePreload(prioritizeNext: true);
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
