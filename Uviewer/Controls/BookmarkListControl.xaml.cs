using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using Uviewer.Models;

namespace Uviewer.Controls
{
    public sealed partial class BookmarkListControl : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(ObservableCollection<BookmarkViewModel>), typeof(BookmarkListControl), new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty EmptyMessageProperty =
            DependencyProperty.Register("EmptyMessage", typeof(string), typeof(BookmarkListControl), new PropertyMetadata(""));

        public ObservableCollection<BookmarkViewModel> ItemsSource
        {
            get => (ObservableCollection<BookmarkViewModel>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public string EmptyMessage
        {
            get => (string)GetValue(EmptyMessageProperty);
            set => SetValue(EmptyMessageProperty, value);
        }

        public bool IsEmpty => ItemsSource == null || ItemsSource.Count == 0;

        public event EventHandler<BookmarkViewModel>? ItemClicked;
        public event EventHandler<BookmarkViewModel>? RemoveClicked;
        public event EventHandler<BookmarkViewModel>? PinClicked;

        public BookmarkListControl()
        {
            this.InitializeComponent();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BookmarkListControl control)
            {
                if (e.OldValue is ObservableCollection<BookmarkViewModel> oldList)
                {
                    oldList.CollectionChanged -= control.List_CollectionChanged;
                }
                if (e.NewValue is ObservableCollection<BookmarkViewModel> newList)
                {
                    newList.CollectionChanged += control.List_CollectionChanged;
                }
                control.UpdateEmptyState();
            }
        }

        private void List_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            Bindings.Update();
        }

        private void BookmarkListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BookmarkViewModel model)
            {
                ItemClicked?.Invoke(this, model);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BookmarkViewModel model)
            {
                RemoveClicked?.Invoke(this, model);
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BookmarkViewModel model)
            {
                PinClicked?.Invoke(this, model);
            }
        }

        private void NameTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is BookmarkViewModel model)
            {
                // Note: FontFamily cannot be easily set via x:Bind in some WinUI environments without careful validation
                // This could be hooked up to settings if needed, similar to original code.
            }
        }
    }

    public class BooleanToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b) return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class ProgressToWidthConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            double progress = value is double d ? d : 0;
            double maxWidth = 280;
            if (parameter != null && double.TryParse(parameter.ToString(), out double p)) maxWidth = p;
            return (progress / 100.0) * maxWidth;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
