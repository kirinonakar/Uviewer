using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace Uviewer.Controls
{
    internal sealed class ToolbarOverflowItemPresentation
    {
        private const double OverflowItemWidth = 220;

        private readonly Visibility _visibility;

        public ToolbarOverflowItemPresentation(
            FrameworkElement element,
            Action<FrameworkElement, FrameworkElement> invoke)
        {
            Element = element;
            _visibility = element.Visibility;
            OverflowElement = CreateOverflowElement(element, invoke);
        }

        public FrameworkElement Element { get; }

        public FrameworkElement OverflowElement { get; }

        public void Apply() => Element.Visibility = Visibility.Collapsed;

        public void Restore() => Element.Visibility = _visibility;

        private static FrameworkElement CreateOverflowElement(
            FrameworkElement element,
            Action<FrameworkElement, FrameworkElement> invoke)
        {
            string label = ResolveLabel(element);
            if (element is not ButtonBase)
            {
                return new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(label) ? element.Name : label,
                    Width = OverflowItemWidth,
                    Padding = new Thickness(12, 8, 12, 8),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            FrameworkElement? icon = CloneButtonVisual(element);
            if (icon != null)
            {
                content.Children.Add(icon);
            }
            content.Children.Add(new TextBlock
            {
                Text = label,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var proxy = new Button
            {
                Content = content,
                Width = OverflowItemWidth,
                MinHeight = 36,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0),
                Background = null,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = element is Control control ? control.IsEnabled : true
            };
            ToolTipService.SetToolTip(proxy, label);
            AutomationProperties.SetName(proxy, label);
            proxy.Click += (_, _) => invoke(element, proxy);
            return proxy;
        }

        private static FrameworkElement? CloneButtonVisual(FrameworkElement element)
        {
            object? content = (element as ContentControl)?.Content;
            return FindVisual(content);
        }

        private static FrameworkElement? FindVisual(object? content)
        {
            if (content is FontIcon fontIcon)
            {
                return new FontIcon
                {
                    Glyph = fontIcon.Glyph,
                    FontFamily = fontIcon.FontFamily,
                    FontSize = fontIcon.FontSize
                };
            }

            if (content is TextBlock textBlock)
            {
                return new TextBlock
                {
                    Text = textBlock.Text,
                    FontFamily = textBlock.FontFamily,
                    FontSize = textBlock.FontSize,
                    FontWeight = textBlock.FontWeight,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            if (content is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    FrameworkElement? visual = FindVisual(child);
                    if (visual != null)
                    {
                        return visual;
                    }
                }
            }

            return null;
        }

        private static string ResolveLabel(FrameworkElement element)
        {
            object? tooltip = ToolTipService.GetToolTip(element);
            string? label = tooltip switch
            {
                string text => text,
                ToolTip toolTip => toolTip.Content?.ToString(),
                _ => tooltip?.ToString()
            };

            if (!string.IsNullOrWhiteSpace(label))
            {
                return label.Replace('\n', ' ');
            }

            label = AutomationProperties.GetName(element);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            return element is TextBlock textBlock ? textBlock.Text : element.Name;
        }
    }

    internal static class ToolbarOverflowLayout
    {
        public static double MeasurePanel(StackPanel panel)
        {
            var visibleChildren = panel.Children
                .OfType<FrameworkElement>()
                .Where(element => element.Visibility == Visibility.Visible)
                .ToList();

            if (visibleChildren.Count == 0)
            {
                return 0;
            }

            double width = visibleChildren.Sum(MeasureElement);
            return width + (panel.Spacing * (visibleChildren.Count - 1));
        }

        public static double MeasureElement(FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible)
            {
                return 0;
            }

            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize.Width;
        }

        public static void OrderOverflowItems(
            StackPanel overflowPanel,
            IEnumerable<FrameworkElement> displayOrder,
            IReadOnlyDictionary<FrameworkElement, ToolbarOverflowItemPresentation> presentations)
        {
            overflowPanel.Children.Clear();
            foreach (FrameworkElement element in displayOrder)
            {
                if (presentations.TryGetValue(element, out ToolbarOverflowItemPresentation? presentation))
                {
                    overflowPanel.Children.Add(presentation.OverflowElement);
                }
            }
        }

        public static void InvokeButton(FrameworkElement element)
        {
            AutomationPeer? peer = element switch
            {
                ToggleButton toggleButton => new ToggleButtonAutomationPeer(toggleButton),
                Button button => new ButtonAutomationPeer(button),
                _ => FrameworkElementAutomationPeer.CreatePeerForElement(element)
            };

            if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
            {
                invokeProvider.Invoke();
                return;
            }

            if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggleProvider)
            {
                toggleProvider.Toggle();
            }
        }
    }
}
