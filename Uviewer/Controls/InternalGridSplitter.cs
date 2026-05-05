using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Uviewer.Controls
{
    public sealed class InternalGridSplitter : Grid
    {
        public static readonly DependencyProperty SplitterBrushProperty =
            DependencyProperty.Register(
                nameof(SplitterBrush),
                typeof(Brush),
                typeof(InternalGridSplitter),
                new PropertyMetadata(null, OnSplitterBrushChanged));

        public static readonly DependencyProperty HoverBrushProperty =
            DependencyProperty.Register(
                nameof(HoverBrush),
                typeof(Brush),
                typeof(InternalGridSplitter),
                new PropertyMetadata(null));

        public static readonly DependencyProperty MinTargetWidthProperty =
            DependencyProperty.Register(
                nameof(MinTargetWidth),
                typeof(double),
                typeof(InternalGridSplitter),
                new PropertyMetadata(150d));

        public static readonly DependencyProperty MaxTargetWidthProperty =
            DependencyProperty.Register(
                nameof(MaxTargetWidth),
                typeof(double),
                typeof(InternalGridSplitter),
                new PropertyMetadata(800d));

        public static readonly DependencyProperty TargetColumnOffsetProperty =
            DependencyProperty.Register(
                nameof(TargetColumnOffset),
                typeof(int),
                typeof(InternalGridSplitter),
                new PropertyMetadata(-1));

        private readonly Border _grip;
        private readonly InputSystemCursor _resizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        private Grid? _ownerGrid;
        private ColumnDefinition? _targetColumn;
        private bool _isDragging;
        private double _dragStartX;
        private double _dragStartWidth;

        public InternalGridSplitter()
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ManipulationMode = ManipulationModes.TranslateX;

            _grip = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Children.Add(_grip);

            Loaded += OnLoaded;
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerCanceled += OnPointerCanceled;
            PointerCaptureLost += OnPointerCaptureLost;
        }

        public event EventHandler? ResizeCompleted;

        public Brush? SplitterBrush
        {
            get => (Brush?)GetValue(SplitterBrushProperty);
            set => SetValue(SplitterBrushProperty, value);
        }

        public Brush? HoverBrush
        {
            get => (Brush?)GetValue(HoverBrushProperty);
            set => SetValue(HoverBrushProperty, value);
        }

        public double MinTargetWidth
        {
            get => (double)GetValue(MinTargetWidthProperty);
            set => SetValue(MinTargetWidthProperty, value);
        }

        public double MaxTargetWidth
        {
            get => (double)GetValue(MaxTargetWidthProperty);
            set => SetValue(MaxTargetWidthProperty, value);
        }

        public int TargetColumnOffset
        {
            get => (int)GetValue(TargetColumnOffsetProperty);
            set => SetValue(TargetColumnOffsetProperty, value);
        }

        private static void OnSplitterBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InternalGridSplitter splitter && !splitter._isDragging)
            {
                splitter._grip.Background = e.NewValue as Brush;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ownerGrid = Parent as Grid;
            _targetColumn = ResolveTargetColumn();
            _grip.Background = SplitterBrush;
            ProtectedCursor = _resizeCursor;
        }

        private ColumnDefinition? ResolveTargetColumn()
        {
            if (_ownerGrid == null)
            {
                return null;
            }

            int splitterColumn = Grid.GetColumn(this);
            int targetColumnIndex = splitterColumn + TargetColumnOffset;

            if (targetColumnIndex < 0 || targetColumnIndex >= _ownerGrid.ColumnDefinitions.Count)
            {
                return null;
            }

            return _ownerGrid.ColumnDefinitions[targetColumnIndex];
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                _grip.Background = HoverBrush ?? SplitterBrush;
            }

            ProtectedCursor = _resizeCursor;
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                _grip.Background = SplitterBrush;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_ownerGrid == null || _targetColumn == null)
            {
                _ownerGrid = Parent as Grid;
                _targetColumn = ResolveTargetColumn();
            }

            if (_ownerGrid == null || _targetColumn == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_ownerGrid);
            _isDragging = true;
            _dragStartX = point.Position.X;
            _dragStartWidth = GetCurrentTargetWidth();
            _grip.Background = HoverBrush ?? SplitterBrush;

            CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || _ownerGrid == null || _targetColumn == null)
            {
                return;
            }

            Point position = e.GetCurrentPoint(_ownerGrid).Position;
            double newWidth = _dragStartWidth + position.X - _dragStartX;
            _targetColumn.Width = new GridLength(ClampTargetWidth(newWidth));
            e.Handled = true;
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            CompleteDrag(e.Pointer);
            e.Handled = true;
        }

        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            CompleteDrag(e.Pointer);
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            CompleteDrag(null);
        }

        private void CompleteDrag(Pointer? pointer)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;

            if (pointer != null)
            {
                ReleasePointerCapture(pointer);
            }

            _grip.Background = SplitterBrush;
            ResizeCompleted?.Invoke(this, EventArgs.Empty);
        }

        private double GetCurrentTargetWidth()
        {
            if (_targetColumn == null)
            {
                return 0;
            }

            if (_targetColumn.Width.IsAbsolute)
            {
                return _targetColumn.Width.Value;
            }

            return _targetColumn.ActualWidth;
        }

        private double ClampTargetWidth(double width)
        {
            double minWidth = Math.Max(MinTargetWidth, _targetColumn?.MinWidth ?? 0);
            double maxWidth = MaxTargetWidth;

            if (_targetColumn != null && !double.IsPositiveInfinity(_targetColumn.MaxWidth))
            {
                maxWidth = Math.Min(maxWidth, _targetColumn.MaxWidth);
            }

            if (maxWidth < minWidth)
            {
                maxWidth = minWidth;
            }

            return Math.Clamp(width, minWidth, maxWidth);
        }
    }
}
