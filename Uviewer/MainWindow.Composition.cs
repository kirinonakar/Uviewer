using System;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            public static void Initialize(MainWindow window, string? launchFilePath)
            {
                var modules = CreateModules();

                window.ApplyCoreServices(CreateCoreServices());

                window.InitializeComponent();

                foreach (var module in modules.InitializeOrder)
                {
                    module.Initialize(window);
                }

                window.RootGrid.SizeChanged += window.RootGrid_SizeChanged;

                try
                {
                    foreach (var step in modules.PostLayoutOrder)
                    {
                        step(window);
                    }

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
