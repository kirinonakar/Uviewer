using System;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            public static void Initialize(MainWindow window, string? launchFilePath)
            {
                window.ApplyCoreServices(CreateCoreServices());

                window.InitializeComponent();
                TextFeatureComposition.Initialize(window);
                EpubFeatureComposition.Initialize(window);
                ImageFeatureComposition.InitializeController(window);
                ShellComposition.InitializeToolbar(window);
                DocumentFeatureComposition.InitializeLocalOpenCoordinator(window);
                WebDavFeatureComposition.Initialize(window);

                window.RootGrid.SizeChanged += window.RootGrid_SizeChanged;

                try
                {
                    ShellComposition.InitializeWindowShell(window);
                    ShellComposition.InitializeWindowControllers(window);
                    DocumentFeatureComposition.InitializeNavigation(window);
                    ImageFeatureComposition.InitializeNavigation(window);
                    ShellComposition.InitializeWindowSettings(window);
                    ShellComposition.InitializeExplorerAndBookmarks(window);
                    ShellComposition.ApplyInitialWindowLayout(window);
                    ShellComposition.InitializeRootInput(window);
                    ShellComposition.InitializeExplorerLists(window);
                    ImageFeatureComposition.InitializePipeline(window);

                    window.ApplyLocalization();
                    window.MainToolbar.SetExternalProgramPath(window._externalProgramPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error initializing MainWindow: {ex.Message}");
                }

                ShellComposition.WireLifecycleEvents(window, launchFilePath);
                ShellComposition.InitializeNotificationTimer(window);
                ImageFeatureComposition.WireImageOptions(window);
            }
        }
    }
}
