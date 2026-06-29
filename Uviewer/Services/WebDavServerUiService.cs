using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Uviewer.Models;

namespace Uviewer.Services
{
    internal sealed record WebDavServerDialogResult(
        bool WasSubmitted,
        bool IsValid,
        WebDavServerInfo? ServerInfo)
    {
        public static WebDavServerDialogResult Cancelled { get; } = new(false, false, null);
        public static WebDavServerDialogResult Invalid { get; } = new(true, false, null);
    }

    internal sealed class WebDavServerUiService
    {
        public async System.Threading.Tasks.Task<WebDavServerDialogResult> ShowAddServerDialogAsync(
            XamlRoot xamlRoot,
            ElementTheme requestedTheme)
        {
            var nameBox = CreateTextBox(Strings.WebDavServerName);
            var addressBox = CreateTextBox(Strings.WebDavAddress);
            var portBox = new NumberBox
            {
                PlaceholderText = Strings.WebDavPort,
                Value = 443,
                Minimum = 1,
                Maximum = 65535,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var idBox = CreateTextBox(Strings.WebDavId);
            var passwordBox = new PasswordBox
            {
                PlaceholderText = Strings.WebDavPassword,
                Margin = new Thickness(0, 0, 0, 0)
            };

            var panel = new StackPanel { Width = 320 };
            AddLabeledInput(panel, Strings.WebDavServerName, nameBox);
            AddLabeledInput(panel, Strings.WebDavAddress, addressBox);
            AddLabeledInput(panel, Strings.WebDavPort, portBox);
            AddLabeledInput(panel, Strings.WebDavId, idBox);
            AddLabeledInput(panel, Strings.WebDavPassword, passwordBox);

            var dialog = new ContentDialog
            {
                Title = Strings.AddWebDavServer,
                Content = panel,
                PrimaryButtonText = Strings.WebDavSave,
                CloseButtonText = Strings.WebDavCancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = requestedTheme
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return WebDavServerDialogResult.Cancelled;
            }

            var serverName = nameBox.Text.Trim();
            var address = addressBox.Text.Trim();
            var port = (int)portBox.Value;
            var userId = idBox.Text.Trim();
            var password = passwordBox.Password;

            if (string.IsNullOrEmpty(serverName) ||
                string.IsNullOrEmpty(address) ||
                string.IsNullOrEmpty(userId))
            {
                return WebDavServerDialogResult.Invalid;
            }

            return new WebDavServerDialogResult(
                true,
                true,
                new WebDavServerInfo
                {
                    ServerName = serverName,
                    Address = address,
                    Port = port,
                    UserId = userId,
                    Password = password
                });
        }

        public void RefreshServerList(
            Panel panel,
            IEnumerable<string> serverNames,
            string? uiFontFamily,
            RoutedEventHandler connectClick,
            RoutedEventHandler deleteClick)
        {
            while (panel.Children.Count > 2)
            {
                panel.Children.RemoveAt(2);
            }

            foreach (var name in serverNames)
            {
                panel.Children.Add(CreateServerListItem(
                    name,
                    uiFontFamily,
                    connectClick,
                    deleteClick));
            }
        }

        private static TextBox CreateTextBox(string placeholder)
        {
            return new TextBox
            {
                PlaceholderText = placeholder,
                Margin = new Thickness(0, 0, 0, 8),
                CharacterCasing = CharacterCasing.Normal,
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false
            };
        }

        private static void AddLabeledInput(StackPanel panel, string label, Control input)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(input);
        }

        private static Grid CreateServerListItem(
            string name,
            string? uiFontFamily,
            RoutedEventHandler connectClick,
            RoutedEventHandler deleteClick)
        {
            var itemGrid = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2)
            };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(uiFontFamily) && uiFontFamily != "Unknown")
            {
                try { nameText.FontFamily = new FontFamily(uiFontFamily); }
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
                        nameText
                    }
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Tag = name
            };
            serverButton.Click += connectClick;
            Grid.SetColumn(serverButton, 0);

            var deleteButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = name
            };
            deleteButton.Click += deleteClick;
            Grid.SetColumn(deleteButton, 1);

            itemGrid.Children.Add(serverButton);
            itemGrid.Children.Add(deleteButton);
            return itemGrid;
        }
    }
}
