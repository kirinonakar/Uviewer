using System;
using System.Collections.Generic;
using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static MainWindowFeatureModules CreateModules()
            {
                var text = new TextFeatureModule();
                var epub = new EpubFeatureModule();
                var image = new ImageFeatureModule();
                var shell = new ShellFeatureModule();
                var document = new DocumentFeatureModule();
                var webDav = new WebDavFeatureModule();

                return new MainWindowFeatureModules(
                    new IMainWindowCompositionModule[]
                    {
                        text,
                        epub,
                        image,
                        shell,
                        document,
                        webDav
                    },
                    new Action<MainWindow>[]
                    {
                        shell.InitializeWindowShell,
                        shell.InitializeWindowControllers,
                        document.InitializeNavigation,
                        image.InitializeNavigation,
                        shell.InitializeWindowSettings,
                        shell.InitializeExplorerAndBookmarks,
                        shell.ApplyInitialWindowLayout,
                        shell.InitializeRootInput,
                        shell.InitializeExplorerLists,
                        image.InitializePipeline,
                        document.InitializeArchiveController
                    });
            }

            private sealed class MainWindowFeatureModules
            {
                public MainWindowFeatureModules(
                    IReadOnlyList<IMainWindowCompositionModule> initializeOrder,
                    IReadOnlyList<Action<MainWindow>> postLayoutOrder)
                {
                    InitializeOrder = initializeOrder;
                    PostLayoutOrder = postLayoutOrder;
                }

                public IReadOnlyList<IMainWindowCompositionModule> InitializeOrder { get; }
                public IReadOnlyList<Action<MainWindow>> PostLayoutOrder { get; }
            }

            private sealed class TextFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window) => TextFeatureComposition.Initialize(window);
            }

            private sealed class EpubFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window) => EpubFeatureComposition.Initialize(window);
            }

            private sealed class ImageFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window) => ImageFeatureComposition.InitializeController(window);

                public void InitializeNavigation(MainWindow window) => ImageFeatureComposition.InitializeNavigation(window);

                public void InitializePipeline(MainWindow window) => ImageFeatureComposition.InitializePipeline(window);
            }

            private sealed class ShellFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window) => ShellComposition.InitializeToolbar(window);

                public void InitializeWindowShell(MainWindow window) => ShellComposition.InitializeWindowShell(window);

                public void InitializeWindowControllers(MainWindow window) => ShellComposition.InitializeWindowControllers(window);

                public void InitializeWindowSettings(MainWindow window) => ShellComposition.InitializeWindowSettings(window);

                public void InitializeExplorerAndBookmarks(MainWindow window) => ShellComposition.InitializeExplorerAndBookmarks(window);

                public void ApplyInitialWindowLayout(MainWindow window) => ShellComposition.ApplyInitialWindowLayout(window);

                public void InitializeRootInput(MainWindow window) => ShellComposition.InitializeRootInput(window);

                public void InitializeExplorerLists(MainWindow window) => ShellComposition.InitializeExplorerLists(window);
            }

            private sealed class DocumentFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window)
                {
                    DocumentFeatureComposition.InitializeLocalOpenCoordinator(window);
                    DocumentFeatureComposition.InitializeFileOpenController(window);
                    DocumentFeatureComposition.InitializeDocumentOpenStateQuery(window);
                    DocumentFeatureComposition.InitializeExplorerItemOperations(window);
                }

                public void InitializeNavigation(MainWindow window) => DocumentFeatureComposition.InitializeNavigation(window);

                public void InitializeArchiveController(MainWindow window) => DocumentFeatureComposition.InitializeArchiveController(window);
            }

            private sealed class WebDavFeatureModule : IMainWindowCompositionModule
            {
                public void Initialize(MainWindow window) => WebDavFeatureComposition.Initialize(window);
            }
        }
    }
}
