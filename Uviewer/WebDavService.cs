using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WebDav;
using Windows.Security.Credentials;

namespace Uviewer
{
    /// <summary>
    /// WebDAV 서버 정보. 서버 이름만 평문, 나머지는 PasswordVault에 암호화 저장.
    /// </summary>
    public class WebDavServerInfo
    {
        public string ServerName { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; } = 443;
        public string UserId { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>
        /// PasswordVault의 Resource Key로 사용할 전체 URL (포트 포함)
        /// </summary>
        public string ResourceUrl => $"https://{Address}:{Port}";

        /// <summary>
        /// 기본 접속 URL
        /// </summary>
        public string BaseUrl => $"https://{Address}:{Port}";
    }

    /// <summary>
    /// WebDAV 원격 항목 정보
    /// </summary>
    public class WebDavItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long ContentLength { get; set; }
        public DateTime? LastModified { get; set; }
    }

    /// <summary>
    /// WebDAV 서비스 레이어. 서버 관리, PasswordVault 암호화 저장, 비동기 파일 작업.
    /// </summary>
    public class WebDavService : IDisposable
    {
        private const string VaultResourcePrefix = "Uviewer_WebDav_";

        private HttpClient? _httpClient;
        private WebDavClient? _webDavClient;
        private WebDavServerInfo? _currentServer;

        public WebDavServerInfo? CurrentServer => _currentServer;
        public bool IsConnected => _webDavClient != null;

        /// <summary>
        /// 실제 URL을 SHA256 해시화하여 PasswordVault Resource 키로 사용.
        /// 자격 증명 관리자에서 원본 주소를 알 수 없게 합니다.
        /// </summary>
        private static string GetVaultResourceKey(string resourceUrl)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceUrl));
            return VaultResourcePrefix + Convert.ToBase64String(hash);
        }

        #region PasswordVault Storage

        private const string ObfuscatedUserName = "Uviewer_Encrypted_Data";
        private const string CredentialSeparator = "|UVIEWER_SECRET_SEP|"; // 단순 텍스트보다 유니크한 구분자 사용

        /// <summary>
        /// 서버 정보를 PasswordVault에 암호화 저장
        /// </summary>
        public void SaveServer(WebDavServerInfo info)
        {
            var vault = new PasswordVault();
            string resource = GetVaultResourceKey(info.ResourceUrl);

            // 기존 항목 삭제 (있으면)
            try
            {
                var existing = vault.FindAllByResource(resource);
                foreach (var cred in existing)
                {
                    vault.Remove(cred);
                }
            }
            catch { /* 없으면 무시 */ }

            // userId와 password를 합쳐서 '비밀번호' 필드에 저장. 사용자 이름은 고정값으로 숨김.
            string combinedSecret = $"{info.UserId}{CredentialSeparator}{info.Password}";
            vault.Add(new PasswordCredential(resource, ObfuscatedUserName, combinedSecret));

            // 서버 이름 → URL 매핑은 DPAPI 암호화 JSON에 저장
            SaveServerNameMapping(info.ServerName, info.ResourceUrl);
        }

        /// <summary>
        /// 저장된 서버 정보를 PasswordVault에서 읽기
        /// </summary>
        public WebDavServerInfo? LoadServer(string serverName)
        {
            var mapping = LoadServerNameMappings();
            if (!mapping.TryGetValue(serverName, out var resourceUrl))
                return null;

            var vault = new PasswordVault();
            string resource = GetVaultResourceKey(resourceUrl);

            try
            {
                var credentials = vault.FindAllByResource(resource);
                if (credentials.Count == 0) return null;

                var cred = credentials[0];
                cred.RetrievePassword();

                string userId = cred.UserName;
                string password = cred.Password;

                // 새 방식: UserName이 고정값이면 Password 필드를 파싱
                if (userId == ObfuscatedUserName)
                {
                    var parts = password.Split(new[] { CredentialSeparator }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        userId = parts[0];
                        password = string.Join(CredentialSeparator, parts.Skip(1)); // 혹시 비밀번호에 구분자가 들어가는 극히 드문 경우 대비
                    }
                }
                // 구 방식: UserName이 실제 ID임 (하위 호환 유지)

                // URL에서 address와 port 추출
                var uri = new Uri(resourceUrl);

                return new WebDavServerInfo
                {
                    ServerName = serverName,
                    Address = uri.Host,
                    Port = uri.Port,
                    UserId = userId,
                    Password = password
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 저장된 서버 정보 삭제
        /// </summary>
        public void DeleteServer(string serverName)
        {
            var mapping = LoadServerNameMappings();
            if (!mapping.TryGetValue(serverName, out var resourceUrl))
                return;

            var vault = new PasswordVault();
            string resource = GetVaultResourceKey(resourceUrl);

            try
            {
                var credentials = vault.FindAllByResource(resource);
                foreach (var cred in credentials)
                {
                    vault.Remove(cred);
                }
            }
            catch { }

            // 서버 이름 매핑 제거
            RemoveServerNameMapping(serverName);
        }

        /// <summary>
        /// 저장된 모든 서버 이름 목록 반환
        /// </summary>
        public List<string> GetSavedServerNames()
        {
            var mapping = LoadServerNameMappings();
            return mapping.Keys.ToList();
        }

        private const string WebDavServersFileName = "webdav_servers.json";
        private string GetWebDavServersFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", WebDavServersFileName);

        private void SaveServerNameMapping(string serverName, string resourceUrl)
        {
            var dict = LoadServerNameMappings();
            dict[serverName] = resourceUrl;
            SaveServerNameMappings(dict);
        }

        private void RemoveServerNameMapping(string serverName)
        {
            var dict = LoadServerNameMappings();
            dict.Remove(serverName);
            SaveServerNameMappings(dict);
        }

        private Dictionary<string, string> LoadServerNameMappings()
        {
            try
            {
                var filePath = GetWebDavServersFilePath();
                if (File.Exists(filePath))
                {
                    var encryptedBase64 = File.ReadAllText(filePath);
                    var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                    // WMI 엔트로피 제거: 속도 향상 및 DPAPI 기본 보안(CurrentUser) 사용
                    var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decryptedBytes);
                    return JsonSerializer.Deserialize(json, WebDavJsonContext.Default.DictionaryStringString)
                           ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading WebDAV servers: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }

        private void SaveServerNameMappings(Dictionary<string, string> dict)
        {
            try
            {
                var filePath = GetWebDavServersFilePath();
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(dict, WebDavJsonContext.Default.DictionaryStringString);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                // WMI 엔트로피 제거
                var encryptedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllText(filePath, Convert.ToBase64String(encryptedBytes));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving WebDAV servers: {ex.Message}");
            }
        }

        #endregion

        #region Connection

        /// <summary>
        /// WebDAV 서버에 HTTPS로 접속
        /// </summary>
        public async Task<bool> ConnectAsync(WebDavServerInfo serverInfo, CancellationToken token = default)
        {
            try
            {
                Disconnect();

                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(serverInfo.UserId, serverInfo.Password),
                    PreAuthenticate = true
                };

                _httpClient = new HttpClient(handler)
                {
                    BaseAddress = new Uri(serverInfo.BaseUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                var clientParams = new WebDavClientParams
                {
                    BaseAddress = new Uri(serverInfo.BaseUrl),
                    Credentials = new NetworkCredential(serverInfo.UserId, serverInfo.Password),
                    PreAuthenticate = true
                };
                _webDavClient = new WebDavClient(clientParams);

                // 접속 테스트: 루트 폴더 목록 가져오기
                var result = await _webDavClient.Propfind("/", new PropfindParameters
                {
                    Headers = new List<KeyValuePair<string, string>>
                    {
                        new("Depth", "0")
                    },
                    CancellationToken = token
                });

                if (result.IsSuccessful)
                {
                    _currentServer = serverInfo;
                    return true;
                }

                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV connect error: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// 접속 해제
        /// </summary>
        public void Disconnect()
        {
            _webDavClient?.Dispose();
            _webDavClient = null;
            _httpClient?.Dispose();
            _httpClient = null;
            _currentServer = null;
        }

        #endregion

        #region File Operations

        /// <summary>
        /// 원격 폴더의 파일/폴더 목록 비동기 조회
        /// </summary>
        public async Task<List<WebDavItem>> ListFolderAsync(string remotePath, CancellationToken token = default)
        {
            if (_webDavClient == null)
                throw new InvalidOperationException("WebDAV 서버에 연결되지 않았습니다.");

            var items = new List<WebDavItem>();

            try
            {
                // Ensure path ends with /
                if (!remotePath.EndsWith("/"))
                    remotePath += "/";

                var result = await _webDavClient.Propfind(remotePath, new PropfindParameters
                {
                    Headers = new List<KeyValuePair<string, string>>
                    {
                        new("Depth", "1")
                    },
                    CancellationToken = token
                });

                if (!result.IsSuccessful)
                {
                    System.Diagnostics.Debug.WriteLine($"WebDAV list error: {result.StatusCode}");
                    return items;
                }

                foreach (var resource in result.Resources)
                {
                    // Skip the directory itself
                    var resourcePath = Uri.UnescapeDataString(resource.Uri ?? "");
                    var normalizedRemotePath = Uri.UnescapeDataString(remotePath ?? "");

                    if (resourcePath.TrimEnd('/') == normalizedRemotePath.TrimEnd('/'))
                        continue;

                    var name = resourcePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    items.Add(new WebDavItem
                    {
                        Name = name,
                        FullPath = resourcePath,
                        IsDirectory = resource.IsCollection,
                        ContentLength = resource.ContentLength ?? 0,
                        LastModified = resource.LastModifiedDate
                    });
                }

                // Sort: directories first, then by name
                items = items
                    .OrderByDescending(i => i.IsDirectory)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV list folder error: {ex.Message}");
            }

            return items;
        }

        private const long MemoryStreamThreshold = 50 * 1024 * 1024; // 50MB

        /// <summary>
        /// 원격 파일을 비동기 스트림으로 다운로드.
        /// 50MB 이하: MemoryStream, 초과: 임시 파일 FileStream 사용.
        /// </summary>
        public async Task<Stream?> DownloadFileAsync(string remotePath, CancellationToken token = default)
        {
            if (_webDavClient == null)
                throw new InvalidOperationException("WebDAV 서버에 연결되지 않았습니다.");

            try
            {
                var response = await _webDavClient.GetRawFile(remotePath, new GetFileParameters
                {
                    CancellationToken = token
                });

                if (response.IsSuccessful && response.Stream != null)
                {
                    // 먼저 MemoryStream에 복사 시도, 임계값 초과 시 파일로 전환
                    var memoryStream = new MemoryStream();
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await response.Stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        totalRead += bytesRead;

                        if (totalRead > MemoryStreamThreshold)
                        {
                            // 임계값 초과 → 임시 파일로 전환
                            var tempDir = Path.Combine(Path.GetTempPath(), "Uviewer_WebDav");
                            Directory.CreateDirectory(tempDir);
                            var tempPath = Path.Combine(tempDir, $"stream_{Guid.NewGuid():N}{Path.GetExtension(remotePath)}");

                            var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
                            // MemoryStream에 이미 읽은 데이터를 파일로 복사
                            memoryStream.Position = 0;
                            await memoryStream.CopyToAsync(fileStream, token);
                            await memoryStream.DisposeAsync();

                            // 나머지를 파일로 계속 쓰기
                            await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                            while ((bytesRead = await response.Stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                            }

                            fileStream.Position = 0;
                            System.Diagnostics.Debug.WriteLine($"WebDAV: Large file ({totalRead + bytesRead} bytes) streamed to temp file.");
                            return fileStream;
                        }

                        await memoryStream.WriteAsync(buffer, 0, bytesRead, token);
                    }

                    memoryStream.Position = 0;
                    return memoryStream;
                }

                System.Diagnostics.Debug.WriteLine($"WebDAV download error: {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV download error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 원격 파일을 임시 파일로 직접 스트리밍 다운로드 (메모리 미사용)
        /// </summary>
        public async Task<string?> DownloadToTempFileAsync(string remotePath, CancellationToken token = default)
        {
            if (_webDavClient == null)
                throw new InvalidOperationException("WebDAV 서버에 연결되지 않았습니다.");

            try
            {
                var response = await _webDavClient.GetRawFile(remotePath, new GetFileParameters
                {
                    CancellationToken = token
                });

                if (!response.IsSuccessful || response.Stream == null)
                {
                    System.Diagnostics.Debug.WriteLine($"WebDAV download error: {response.StatusCode}");
                    return null;
                }

                var fileName = Path.GetFileName(remotePath.TrimEnd('/'));
                var tempDir = Path.Combine(Path.GetTempPath(), "Uviewer_WebDav");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, fileName);

                using var fileStream = File.Create(tempPath);
                await response.Stream.CopyToAsync(fileStream, token);

                return tempPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV temp download error: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
            CleanupTempFiles();
        }

        /// <summary>
        /// WebDAV 임시 다운로드 폴더 정리
        /// </summary>
        public static void CleanupTempFiles()
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Uviewer_WebDav");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    System.Diagnostics.Debug.WriteLine("WebDAV temp folder cleaned up.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cleaning WebDAV temp: {ex.Message}");
            }
        }
    }

    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class WebDavJsonContext : JsonSerializerContext
    {
    }
}
