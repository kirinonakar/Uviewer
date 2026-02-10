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
        private static bool _allowMultipleInstances = true;
        private static CancellationTokenSource? _pipeCts;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.UnhandledException += App_UnhandledException;

            try
            {
                // Set Windows App SDK runtime base directory for single-file publish
                Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
                
                // Load settings
                LoadSettings();

                // Get command line arguments
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
            InitializeComponent();
        }

        private void LoadSettings()
        {
            try
            {
                string settingsFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", "window_settings.txt");
                if (System.IO.File.Exists(settingsFile))
                {
                    var lines = File.ReadAllLines(settingsFile);
                    if (lines.Length >= 11)
                    {
                        if (lines[10].Trim() == "0") _allowMultipleInstances = false;
                    }
                }
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

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _window = new MainWindow(LaunchFilePath);
                _window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error launching window: {ex.Message}\n{ex.StackTrace}");
                MessageBox(IntPtr.Zero, $"Critical Error Launching App:\n{ex.Message}\n\n{ex.StackTrace}", "Uviewer Startup Error", 0x10);
            }
        }
    }
}
