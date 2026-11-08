using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebDav;
using Uviewer.Services;
using Windows.Security.Credentials;

namespace Uviewer
{
    public sealed record WebDavCertificateError(string Authority, string Fingerprint, SslPolicyErrors Errors);

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
    public class WebDavService : IDisposable, IAsyncDisposable
    {
        private const string VaultResourcePrefix = "Uviewer_WebDav_";

        private HttpClient? _httpClient;
        private WebDavClient? _webDavClient;
        private WebDavServerInfo? _currentServer;
        private readonly object _tempGate = new();
        private readonly RemoteFileDownloadCache _downloadCache = new();
        private LeasedTempDirectory? _tempDirectory;
        private readonly List<FileStream> _tempStreams = new();
        private bool _disposed;

        private string GetTempFolder()
        {
            lock (_tempGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _tempDirectory ??= LeasedTempDirectory.Create(Path.Combine(Path.GetTempPath(), "Uviewer", "WebDav"), "instance_");
                return _tempDirectory.Folder;
            }
        }

        private FileStream CreateTempStream(string path, FileAccess access, FileOptions options = FileOptions.None)
        {
            lock (_tempGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.Create, access, FileShare.None, 81920, options);
                _tempStreams.RemoveAll(previous => !previous.CanRead && !previous.CanWrite);
                _tempStreams.Add(stream);
                return stream;
            }
        }

        private void CloseTempStreams()
        {
            foreach (var stream in _tempStreams)
            {
                try { stream.Dispose(); }
                catch (IOException ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
            _tempStreams.Clear();
        }

        public WebDavServerInfo? CurrentServer => _currentServer;
        public bool IsConnected => _webDavClient != null;
        public WebDavCertificateError? CertificateError { get; private set; }

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
            _serverMappings.Update(mapping =>
            {
                var vault = new PasswordVault();
                string resource = GetVaultResourceKey(info.ResourceUrl);
                string combinedSecret = $"{info.UserId}{CredentialSeparator}{info.Password}";
                vault.Add(new PasswordCredential(resource, info.ServerName, combinedSecret));
                mapping[info.ServerName] = info.ResourceUrl;
            });
        }

        /// <summary>
        /// 저장된 서버 정보를 PasswordVault에서 읽기
        /// </summary>
        public WebDavServerInfo? LoadServer(string serverName)
        {
            var mapping = _serverMappings.Read();
            if (!mapping.TryGetValue(serverName, out var resourceUrl))
                return null;

            var vault = new PasswordVault();
            string resource = GetVaultResourceKey(resourceUrl);

            try
            {
                var credentials = vault.FindAllByResource(resource);
                if (credentials.Count == 0) return null;

                // 1. 해당 서버 이름(UserName)을 가진 자격 증명 우선 검색
                // 2. 없으면 구버전 호환용(ObfuscatedUserName) 검색
                var cred = credentials.FirstOrDefault(c => c.UserName == serverName)
                           ?? credentials.FirstOrDefault(c => c.UserName == ObfuscatedUserName);

                if (cred == null) return null;
                cred.RetrievePassword();

                string userName = cred.UserName;
                string password = cred.Password;

                string realUserId = userName;
                string realPassword = password;

                // 새 방식(서버 이름이 UserName) 또는 구 방식(ObfuscatedUserName이 UserName) 모두 Password 필드 파싱 시도
                if (password.Contains(CredentialSeparator))
                {
                    var parts = password.Split(new[] { CredentialSeparator }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        realUserId = parts[0];
                        realPassword = string.Join(CredentialSeparator, parts.Skip(1));
                    }
                }

                // URL에서 address와 port 추출
                if (!resourceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    resourceUrl = "https://" + resourceUrl;
                }
                var uri = new Uri(resourceUrl);

                return new WebDavServerInfo
                {
                    ServerName = serverName,
                    Address = uri.Host,
                    Port = uri.Port,
                    UserId = realUserId,
                    Password = realPassword
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
            _serverMappings.Update(mapping =>
            {
                if (!mapping.TryGetValue(serverName, out var resourceUrl))
                    return;

                var vault = new PasswordVault();
                string resource = GetVaultResourceKey(resourceUrl);

                try
                {
                    var credentials = vault.FindAllByResource(resource);

                    // 1. 해당 서버 이름을 가진 자격 증명만 삭제
                    var specificCred = credentials.FirstOrDefault(c => c.UserName == serverName);
                    if (specificCred != null)
                    {
                        vault.Remove(specificCred);
                    }

                    // 2. 구버전 마이그레이션용(ObfuscatedUserName) 삭제 여부 결정
                    // 해당 주소를 사용하는 다른 서버가 없는 경우에만 삭제하여 다른 서버 간섭 방지
                    bool otherUsesSameUrl = mapping.Any(kvp => kvp.Key != serverName && kvp.Value == resourceUrl);
                    if (!otherUsesSameUrl)
                    {
                        var oldCred = credentials.FirstOrDefault(c => c.UserName == ObfuscatedUserName);
                        if (oldCred != null)
                        {
                            vault.Remove(oldCred);
                        }
                    }
                }
                catch { }

                // 서버 이름 매핑 제거
                mapping.Remove(serverName);
            });
        }

        /// <summary>
        /// 저장된 모든 서버 이름 목록 반환
        /// </summary>
        public List<string> GetSavedServerNames()
        {
            var mapping = _serverMappings.Read();
            return mapping.Keys.ToList();
        }

        private readonly WebDavServerMappingStore _serverMappings = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "webdav_servers.json"));

        #endregion

        #region Connection

        /// <summary>
        /// WebDAV 서버에 HTTPS로 접속
        /// </summary>
        public async Task<bool> ConnectAsync(WebDavServerInfo serverInfo, CancellationToken token = default,
            WebDavCertificateError? approvedCertificate = null)
        {
            WebDavCertificateError? rejectedCertificate = null;
            try
            {
                Disconnect();
                token.ThrowIfCancellationRequested();

                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(serverInfo.UserId, serverInfo.Password),
                    PreAuthenticate = true,
                    ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
                    {
                        if (errors == SslPolicyErrors.None)
                            return true;

                        // Approval applies only to this endpoint and certificate for this connection.
                        var error = new WebDavCertificateError(
                            request.RequestUri!.Authority,
                            certificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? "",
                            errors);
                        if (certificate != null && approvedCertificate != null &&
                            string.Equals(error.Authority, approvedCertificate.Authority, StringComparison.OrdinalIgnoreCase) &&
                            error.Fingerprint == approvedCertificate.Fingerprint &&
                            (errors & ~approvedCertificate.Errors) == SslPolicyErrors.None)
                            return true;

                        rejectedCertificate = error;
                        return false;
                    }
                };

                _httpClient = new HttpClient(handler)
                {
                    BaseAddress = new Uri(serverInfo.BaseUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                // Share the handler for connection checks, folder listings and both download paths.
                _webDavClient = new WebDavClient(_httpClient);

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
                CertificateError = rejectedCertificate;
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Disconnect();
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV connect error: {ex.Message}");
                Disconnect();
                CertificateError = rejectedCertificate;
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
            CertificateError = null;
        }

        #endregion

        #region File Operations

        /// <summary>
        /// 원격 폴더의 파일/폴더 목록 비동기 조회
        /// </summary>
        public async Task<List<WebDavItem>> ListFolderAsync(string remotePath, CancellationToken token = default)
        {
            if (_webDavClient == null)
                throw new InvalidOperationException(Strings.WebDavNotConnected);

            var items = new List<WebDavItem>();

            try
            {
                // Ensure path ends with /
                if (!remotePath.EndsWith("/"))
                    remotePath += "/";

                string baseUrl = _httpClient?.BaseAddress?.ToString() ?? "";
                string baseUrlAlt = baseUrl.TrimEnd('/');

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
                    var rawUri = resource.Uri ?? "";
                    string resourcePath = Uri.UnescapeDataString(rawUri);

                    // Remove full URL if present to get path only
                    if (!string.IsNullOrEmpty(baseUrl) && resourcePath.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        resourcePath = resourcePath.Substring(baseUrl.Length);
                    }
                    else if (!string.IsNullOrEmpty(baseUrlAlt) && resourcePath.StartsWith(baseUrlAlt, StringComparison.OrdinalIgnoreCase))
                    {
                        resourcePath = resourcePath.Substring(baseUrlAlt.Length);
                    }

                    if (!resourcePath.StartsWith("/")) resourcePath = "/" + resourcePath;

                    var normalizedRemotePath = Uri.UnescapeDataString(remotePath ?? "");
                    if (!normalizedRemotePath.StartsWith("/")) normalizedRemotePath = "/" + normalizedRemotePath;

                    // Skip the directory itself (case-insensitive for some servers)
                    if (resourcePath.TrimEnd('/').Equals(normalizedRemotePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = resource.DisplayName ?? resourcePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
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
                    .ThenBy(i => i.Name, NaturalSortComparer.Default)
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
                throw new InvalidOperationException(Strings.WebDavNotConnected);

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
                            var tempDir = await Task.Run(GetTempFolder, token).ConfigureAwait(false);
                            var tempPath = Path.Combine(tempDir, $"stream_{Guid.NewGuid():N}{Path.GetExtension(remotePath)}");

                            var fileStream = CreateTempStream(tempPath, FileAccess.ReadWrite, FileOptions.DeleteOnClose);
                            try
                            {
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
                                System.Diagnostics.Debug.WriteLine($"WebDAV: Large file ({totalRead} bytes) streamed to temp file.");
                                return fileStream;
                            }
                            catch
                            {
                                await fileStream.DisposeAsync();
                                throw;
                            }
                        }

                        await memoryStream.WriteAsync(buffer, 0, bytesRead, token);
                    }

                    memoryStream.Position = 0;
                    System.Diagnostics.Debug.WriteLine($"WebDAV: File ({totalRead} bytes) downloaded to memory.");
                    return memoryStream;
                }

                System.Diagnostics.Debug.WriteLine($"WebDAV download error: {response.StatusCode} for {remotePath}");
                return null;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("WebDAV download canceled.");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV download exception: {ex.Message} source: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 원격 파일을 임시 파일로 직접 스트리밍 다운로드 (메모리 미사용)
        /// </summary>
        public async Task<string?> DownloadToTempFileAsync(string remotePath, CancellationToken token = default)
        {
            var client = _httpClient;
            var server = _currentServer;
            if (client == null || server == null)
                throw new InvalidOperationException(Strings.WebDavNotConnected);

            try
            {
                var remoteUri = new Uri(client.BaseAddress!, remotePath);
                var fileName = Path.GetFileName(remotePath.TrimEnd('/'));
                var tempDir = await Task.Run(GetTempFolder, token).ConfigureAwait(false);
                // Separate accounts as well as servers and remote paths.
                var urlKey = $"{server.UserId}\n{remoteUri.AbsoluteUri}";
                var stableHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(urlKey)));
                var tempPath = Path.Combine(tempDir, stableHash, fileName);

                return await _downloadCache.DownloadAsync(
                    client, remoteUri, tempPath,
                    path => CreateTempStream(path, FileAccess.Write, FileOptions.Asynchronous),
                    (partial, completed) =>
                    {
                        lock (_tempGate)
                        {
                            ObjectDisposedException.ThrowIf(_disposed, this);
                            File.Move(partial, completed, overwrite: true);
                        }
                    }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("WebDAV temp download canceled.");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV temp download exception: {ex.Message}");
                return null;
            }
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
            lock (_tempGate)
            {
                _disposed = true;
                CloseTempStreams();
                _tempDirectory?.Dispose();
                _tempDirectory = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Disconnect();
            LeasedTempDirectory? directory;
            lock (_tempGate)
            {
                _disposed = true;
                CloseTempStreams();
                directory = _tempDirectory;
                _tempDirectory = null;
            }
            if (directory != null) await directory.DeleteAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// WebDAV 임시 다운로드 폴더 정리
        /// </summary>
        public static Task CleanupTempFilesAsync() => Task.Run(() =>
            LeasedTempDirectory.CleanupAbandoned(Path.Combine(Path.GetTempPath(), "Uviewer", "WebDav"), "*"));

        /// <summary>동기 래퍼 (하위 호환)</summary>
        public static void CleanupTempFiles()
        {
            _ = CleanupTempFilesAsync();
        }
    }

}
