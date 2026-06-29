using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Uviewer
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static string? LaunchFilePath { get; set; }
        private readonly Services.AppSettingsService _appSettingsService = new();
        private static bool _allowMultipleInstances = true;
        private static bool _isRegistered = false;
        private static CancellationTokenSource? _pipeCts;
        private uint _comCookie;
        private static bool _isComActivation = false;
        private static System.Threading.Timer? _exitTimer;
        private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
        private static int _comExitRequested;
        private static int _comExitCompleted;
        private static int _comActiveCallCount;
        private static int _comServerLockCount;
        private static int _comInvokeCompleted;
        private static long _comStartedTicks;
        private const int ComIdleExitTimeoutMs = 60000;
        private const int ComMaxLifetimeMs = 300000;
        private const int ComExitFallbackDelayMs = 1500;
        private const int ComPostInvokeExitDelayMs = 3000;

        // 추가 1: GC(가비지 컬렉터) 수집을 방지하기 위한 정적 변수
        private static UviewerExplorerCommandFactory? _commandFactory;

        [DllImport("ole32.dll")]
        private static extern int CoRegisterClassObject(ref Guid rclsid, IntPtr pUnk, uint dwClsContext, uint flags, out uint lpdwCookie);

        [DllImport("ole32.dll")]
        private static extern int CoRevokeClassObject(uint dwCookie);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        public App()
        {
            this.UnhandledException += App_UnhandledException;
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

            // COM 실행 여부와 상관없이 InitializeComponent()는 호출해 주는 것이 WinUI 3 생명주기에 안전합니다.
            InitializeComponent();

            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // args.Any() 대신 CommandLine 전체 문자열 검사가 더 안정적입니다.
            string cmdLine = Environment.CommandLine;
            if (cmdLine.Contains("-Embedding", StringComparison.OrdinalIgnoreCase))
            {
                _isComActivation = true;
                _comStartedTicks = DateTime.UtcNow.Ticks;
                try
                {
                    Guid clsid = Guid.Parse("D9614E4F-E02D-4E3F-8C3B-76C1B323E0B9");

                    // 정적 변수에 할당하여 앱이 살아있는 동안 Factory가 삭제되지 않도록 보호
                    _commandFactory = new UviewerExplorerCommandFactory();
                    IntPtr factoryPtr = Marshal.GetIUnknownForObject(_commandFactory);
                    try
                    {
                        int hr = CoRegisterClassObject(ref clsid, factoryPtr, 4, 1, out _comCookie);
                        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    }
                    finally
                    {
                        Marshal.Release(factoryPtr);
                    }

                    _exitTimer = new System.Threading.Timer((_) =>
                    {
                        RequestComExit(0);
                    }, null, ComIdleExitTimeoutMs, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"COM Registration Error: {ex.Message}");
                    Environment.Exit(1); // 오류 발생 시에도 확실히 종료
                }
            }
            else
            {
                // 일반 실행 모드 (InitializeComponent는 위에서 이미 처리함)
                try
                {
                    LoadSettings();

                    var args = Environment.GetCommandLineArgs();
                    if (args.Length > 1)
                    {
                        LaunchFilePath = args[1];
                    }

                    if (!_allowMultipleInstances)
                    {
                        if (TrySendToExistingInstance(LaunchFilePath))
                        {
                            Environment.Exit(0);
                            return;
                        }
                        StartPipeServer();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in App constructor: {ex.Message}");
                }
            }
        }

        private void RequestComExit(int exitCode)
        {
            if (Volatile.Read(ref _comExitCompleted) != 0) return;
            if (ShouldDeferComExit())
            {
                ScheduleComExitTimer();
                return;
            }

            if (Interlocked.Exchange(ref _comExitRequested, 1) != 0) return;

            bool queued = false;
            try
            {
                queued = _dispatcherQueue?.TryEnqueue(() => CompleteComExit(exitCode)) == true;
            }
            catch { }

            if (!queued)
            {
                CompleteComExit(exitCode);
                return;
            }

            // Dispatcher가 종료 직전에 멈춰 있으면 TryEnqueue가 성공해도 실행되지 않을 수 있습니다.
            // 이 보조 경로가 COM 전용 프로세스가 한 코어를 잡고 남는 상황을 끝냅니다.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(ComExitFallbackDelayMs);
                if (Volatile.Read(ref _comExitCompleted) == 0)
                {
                    CompleteComExit(exitCode);
                }
            });
        }

        private void CompleteComExit(int exitCode)
        {
            if (ShouldDeferComExit())
            {
                Interlocked.Exchange(ref _comExitRequested, 0);
                ScheduleComExitTimer();
                return;
            }

            if (Interlocked.Exchange(ref _comExitCompleted, 1) != 0) return;
            Interlocked.Exchange(ref _exitTimer, null)?.Dispose();

            try
            {
                if (_comCookie != 0)
                {
                    CoRevokeClassObject(_comCookie);
                    _comCookie = 0;
                }
            }
            catch { }

            _commandFactory = null;
            TerminateCurrentProcess(exitCode);
        }

        private static void TerminateCurrentProcess(int exitCode)
        {
            try
            {
                TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode));
            }
            catch
            {
                Environment.Exit(exitCode);
            }
        }

        private static bool ShouldDeferComExit()
        {
            if (IsComMaxLifetimeExceeded()) return false;

            if (Volatile.Read(ref _comActiveCallCount) > 0)
            {
                return true;
            }

            // After the command has launched the viewer process, Explorer may keep the
            // class factory locked longer than the command actually needs. Do not let
            // that stale lock keep the COM-only process alive.
            return Volatile.Read(ref _comInvokeCompleted) == 0 &&
                Volatile.Read(ref _comServerLockCount) > 0;
        }

        private static bool IsComMaxLifetimeExceeded()
        {
            long startedTicks = Volatile.Read(ref _comStartedTicks);
            if (startedTicks <= 0) return false;

            long elapsedTicks = DateTime.UtcNow.Ticks - startedTicks;
            return elapsedTicks >= TimeSpan.FromMilliseconds(ComMaxLifetimeMs).Ticks;
        }

        private static void ScheduleComExitTimer()
        {
            if (!_isComActivation) return;
            if (Volatile.Read(ref _comExitCompleted) != 0) return;

            try
            {
                _exitTimer?.Change(ComIdleExitTimeoutMs, Timeout.Infinite);
            }
            catch { }
        }

        public static void EnterComCall()
        {
            if (!_isComActivation) return;

            Interlocked.Increment(ref _comActiveCallCount);
            MarkActivity();
        }

        public static void LeaveComCall()
        {
            if (!_isComActivation) return;

            if (Interlocked.Decrement(ref _comActiveCallCount) < 0)
            {
                Interlocked.Exchange(ref _comActiveCallCount, 0);
            }
            MarkActivity();
        }

        public static void SetComServerLock(bool locked)
        {
            if (!_isComActivation) return;

            if (locked)
            {
                Interlocked.Increment(ref _comServerLockCount);
            }
            else if (Interlocked.Decrement(ref _comServerLockCount) < 0)
            {
                Interlocked.Exchange(ref _comServerLockCount, 0);
            }

            MarkActivity();
        }

        public static void NotifyComInvokeCompleted()
        {
            if (!_isComActivation) return;
            if (Volatile.Read(ref _comExitCompleted) != 0) return;

            Interlocked.Exchange(ref _comInvokeCompleted, 1);

            try
            {
                _exitTimer?.Change(ComPostInvokeExitDelayMs, Timeout.Infinite);
            }
            catch { }
        }

        private static long _lastActivityTicks = 0;
        public static void MarkActivity()
        {
            if (!_isComActivation) return;
            if (Volatile.Read(ref _comExitCompleted) != 0) return;
            
            // 1초 미만의 짧은 주기로 호출될 경우 타이머 리셋 무시 (부하 감소)
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastActivityTicks < TimeSpan.TicksPerSecond) return;
            _lastActivityTicks = now;
            
            try
            {
                _exitTimer?.Change(ComIdleExitTimeoutMs, Timeout.Infinite);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _appSettingsService.LoadSettings();
                _allowMultipleInstances = settings.AllowMultipleInstances;
                _isRegistered = settings.IsRegistered;
            }
            catch { }
        }

        private void SaveRegistrationStatus()
        {
            try
            {
                _appSettingsService.SaveRegistrationStatus(isRegistered: true, allowMultipleInstances: _allowMultipleInstances);
            }
            catch { }
        }

        private bool TrySendToExistingInstance(string? filePath)
        {
            try
            {
                // Check if already running by trying to connect to the pipe
                using var client = new NamedPipeClientStream(".", "UviewerInstancePipe", PipeDirection.Out);
                client.Connect(200); // 200ms timeout
                using var writer = new StreamWriter(client);
                writer.WriteLine(filePath ?? "");
                writer.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartPipeServer()
        {
            _pipeCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!_pipeCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream("UviewerInstancePipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(_pipeCts.Token);
                        using var reader = new StreamReader(server);
                        var filePath = await reader.ReadLineAsync();
                        
                        // Wait for window to be initialized if it's not yet
                        int retryCount = 0;
                        while (_window == null && retryCount < 50) 
                        {
                            await Task.Delay(100);
                            retryCount++;
                        }

                        if (_window is MainWindow mainWindow)
                        {
                            mainWindow.DispatcherQueue.TryEnqueue(async () =>
                            {
                                await mainWindow.HandleNewInstanceFile(filePath);
                            });
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Pipe server error: {ex.Message}");
                        await Task.Delay(1000);
                    }
                }
            });
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            MessageBox(IntPtr.Zero, $"Unhandled Error:\n{e.Message}\n\n{e.Exception.StackTrace}", "Uviewer Fatal Error", 0x10);
            e.Handled = true; // Prevent immediate termination to show message
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (_isComActivation) return;
            try
            {
                RegisterFileAssociations();
                _window = new MainWindow(LaunchFilePath);
                _window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching window: {ex.Message}\n{ex.StackTrace}");
                MessageBox(IntPtr.Zero, $"Critical Error Launching App:\n{ex.Message}\n\n{ex.StackTrace}", "Uviewer Startup Error", 0x10);
            }
        }

        private void RegisterFileAssociations()
        {
            // Packaged 앱(MSIX)은 Manifest에서 이미 연결되어 있으므로 중복 등록을 피합니다.
            // 이렇게 하면 매번 실행 시 발생하는 쉘 리프레시를 원천적으로 방지할 수 있습니다.
            if (IsPackaged())
            {
                _isRegistered = true;
                SaveRegistrationStatus();
                return;
            }

            string executablePath = GetExecutablePath();
            if (_isRegistered && IsOpenWithApplicationMetadataCurrent(executablePath)) return;

            try
            {
                string[] extensions = { 
                    ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".jxl", ".ico", ".tiff", ".tif", 
                    ".txt", ".html", ".htm", ".md", ".xml", 
                    ".zip", ".rar", ".7z", ".tar", ".gz", ".cbz", ".cbr", 
                    ".epub", ".pdf" 
                };
                string? logoPath = GetAssociationLogoPath();
                ActivationRegistrationManager.RegisterForFileTypeActivation(extensions, logoPath, "Uviewer", null, executablePath);
                RegisterOpenWithApplicationMetadata(executablePath);
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
                
                _isRegistered = true;
                SaveRegistrationStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering file associations: {ex.Message}");
            }
        }

        private static string GetExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                return Environment.ProcessPath;
            }

            return System.IO.Path.Combine(AppContext.BaseDirectory, "Uviewer.exe");
        }

        private static string? GetAssociationLogoPath()
        {
            string assetsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets");
            string[] candidates =
            {
                "Uviewer.ico",
                "Uviewer2.png"
            };

            foreach (string candidate in candidates)
            {
                string path = System.IO.Path.Combine(assetsDirectory, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static string GetExecutableIconReference(string executablePath)
        {
            return $"\"{executablePath}\",0";
        }

        private static bool IsOpenWithApplicationMetadataCurrent(string executablePath)
        {
            try
            {
                string applicationKeyPath = GetOpenWithApplicationKeyPath(executablePath);
                using RegistryKey? applicationKey = Registry.CurrentUser.OpenSubKey(applicationKeyPath);
                string? applicationIcon = applicationKey?.GetValue("ApplicationIcon") as string;
                return string.Equals(applicationIcon, GetExecutableIconReference(executablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void RegisterOpenWithApplicationMetadata(string executablePath)
        {
            try
            {
                string applicationKeyPath = GetOpenWithApplicationKeyPath(executablePath);
                string iconReference = GetExecutableIconReference(executablePath);
                using RegistryKey? applicationKey = Registry.CurrentUser.CreateSubKey(applicationKeyPath);
                applicationKey?.SetValue("ApplicationName", "Uviewer", RegistryValueKind.String);
                applicationKey?.SetValue("ApplicationDescription", "Uviewer", RegistryValueKind.String);
                applicationKey?.SetValue("ApplicationIcon", iconReference, RegistryValueKind.String);

                using RegistryKey? defaultIconKey = applicationKey?.CreateSubKey("DefaultIcon");
                defaultIconKey?.SetValue("", iconReference, RegistryValueKind.String);

                using RegistryKey? commandKey = applicationKey?.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue("", $"\"{executablePath}\" \"%1\"", RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering Open With metadata: {ex.Message}");
            }
        }

        private static string GetOpenWithApplicationKeyPath(string executablePath)
        {
            return $@"Software\Classes\Applications\{System.IO.Path.GetFileName(executablePath)}";
        }

        private bool IsPackaged()
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
