using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;
using Windows.Graphics;

namespace Uviewer.Services
{
    internal static class TextWindowLayoutService
    {
        private const int MinTextWindowWidth = 800;
        private const int MinTextWindowHeight = 600;
        private const double MinTextContentWidth = 500;
        private const double MinSidebarWidth = 200;

        internal static void EnforceMinWindowSize(AppWindow appWindow, WindowStateManager windowState, bool isTextLikeMode)
        {
            if (!isTextLikeMode || windowState.IsFullscreen) return;
            if (appWindow.Size.Width >= MinTextWindowWidth && appWindow.Size.Height >= MinTextWindowHeight) return;

            appWindow.Resize(new SizeInt32(
                Math.Max(appWindow.Size.Width, MinTextWindowWidth),
                Math.Max(appWindow.Size.Height, MinTextWindowHeight)));
        }

        internal static void ConstrainSidebarForTextMode(
            Size newRootSize,
            bool isTextLikeMode,
            bool isSidebarVisible,
            ColumnDefinition sidebarColumn)
        {
            if (isTextLikeMode && isSidebarVisible)
            {
                double maxSidebarWidth = Math.Max(MinSidebarWidth, newRootSize.Width - MinTextContentWidth);

                if (Math.Abs(sidebarColumn.MaxWidth - maxSidebarWidth) > 1.0)
                {
                    sidebarColumn.MaxWidth = maxSidebarWidth;
                }

                if (sidebarColumn.Width.IsAbsolute && sidebarColumn.Width.Value > maxSidebarWidth)
                {
                    sidebarColumn.Width = new GridLength(maxSidebarWidth);
                }

                return;
            }

            if (sidebarColumn.MaxWidth != double.PositiveInfinity)
            {
                sidebarColumn.MaxWidth = double.PositiveInfinity;
            }
        }

        internal static void EnsureMinWindowSizeForText(
            AppWindow appWindow,
            Grid rootGrid,
            WindowStateManager windowState,
            ColumnDefinition sidebarColumn)
        {
            if (windowState.IsFullscreen) return;

            var currentSize = appWindow.Size;
            bool needsResize = false;
            int newWidth = currentSize.Width;
            int newHeight = currentSize.Height;

            if (currentSize.Width < MinTextWindowWidth)
            {
                newWidth = MinTextWindowWidth;
                needsResize = true;
            }

            if (currentSize.Height < MinTextWindowHeight)
            {
                newHeight = MinTextWindowHeight;
                needsResize = true;
            }

            if (needsResize)
            {
                appWindow.Resize(new SizeInt32(newWidth, newHeight));
            }

            if (!windowState.IsSidebarVisible) return;

            double estimatedClientWidth = needsResize ? newWidth - 16 : rootGrid.ActualWidth;
            if (estimatedClientWidth <= 0) estimatedClientWidth = newWidth;

            double maxAllowedSidebar = Math.Max(MinSidebarWidth, estimatedClientWidth - MinTextContentWidth);

            if (sidebarColumn.ActualWidth > maxAllowedSidebar || sidebarColumn.Width.Value > maxAllowedSidebar)
            {
                sidebarColumn.Width = new GridLength(maxAllowedSidebar);
            }

            sidebarColumn.MaxWidth = maxAllowedSidebar;
        }
    }
}
