using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace FlowerPlayer.Controls
{
    public sealed partial class RangeSlider : UserControl
    {
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register("Minimum", typeof(double), typeof(RangeSlider), new PropertyMetadata(0.0, OnPropertyChanged));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum", typeof(double), typeof(RangeSlider), new PropertyMetadata(100.0, OnPropertyChanged));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(RangeSlider), new PropertyMetadata(0.0, OnPropertyChanged));
        public static readonly DependencyProperty RangeStartProperty = DependencyProperty.Register("RangeStart", typeof(double), typeof(RangeSlider), new PropertyMetadata(0.0, OnPropertyChanged));
        public static readonly DependencyProperty RangeEndProperty = DependencyProperty.Register("RangeEnd", typeof(double), typeof(RangeSlider), new PropertyMetadata(100.0, OnPropertyChanged));

        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double RangeStart { get => (double)GetValue(RangeStartProperty); set => SetValue(RangeStartProperty, value); }
        public double RangeEnd { get => (double)GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }

        private bool _isDragging;

        public RangeSlider()
        {
            this.InitializeComponent();
            MainSlider.Minimum = Minimum;
            MainSlider.Maximum = Maximum;
            MainSlider.Value = Value;
            SizeChanged += RangeSlider_SizeChanged;
            
            // Track when user is dragging the slider
            MainSlider.PointerPressed += (s, e) => 
            {
                _isUserDraggingSlider = true;
                e.Handled = false; // Allow event to propagate to Thumb so it can move
            };
            MainSlider.PointerReleased += (s, e) =>
            {
                _isUserDraggingSlider = false;
                e.Handled = false;
                // Force update only if value actually changed significantly to avoid jitter
                if (Math.Abs(MainSlider.Value - Value) > 0.001)
                {
                    Value = MainSlider.Value;
                    ValueChanging?.Invoke(this, Value);
                }
                UpdateVisuals(); 
            };
            MainSlider.PointerCaptureLost += (s, e) =>
            {
                _isUserDraggingSlider = false;
                UpdateVisuals(); 
            };
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var slider = d as RangeSlider;
            slider?.UpdateVisuals();
        }

        private void RangeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (ActualWidth == 0 || Maximum <= Minimum) return;

            MainSlider.Minimum = Minimum;
            MainSlider.Maximum = Maximum;
            
            // Update MainSlider value if not being dragged by user
            if (!_isUserDraggingSlider)
            {
                _isUpdatingFromProperty = true;
                MainSlider.Value = Value;
                _isUpdatingFromProperty = false;
            }

            double range = Maximum - Minimum;
            double width = ActualWidth - 20; // Margin correction

            double startX = ((RangeStart - Minimum) / range) * width + 10;
            double endX = ((RangeEnd - Minimum) / range) * width + 10;

            Canvas.SetLeft(StartThumbCanvas, startX);
            Canvas.SetLeft(EndThumbCanvas, endX);

            // Update Highlight
            double highlightWidth = endX - startX;
            if (highlightWidth < 0) highlightWidth = 0;
            RangeHighlight.Width = highlightWidth;
            RangeHighlight.Margin = new Thickness(startX, 0, 0, 0);
        }

        private bool _isUserDraggingSlider = false;
        private bool _isUpdatingFromProperty = false;

        private void MainSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Prevent feedback loop
            if (_isUpdatingFromProperty) return;
            
            // Always notify ViewModel when slider value changes by user interaction
            // The PointerPressed/Released flags are not enough because ValueChanged fires during drag
            if (MainSlider.FocusState != FocusState.Unfocused || _isUserDraggingSlider)
            {
                Value = e.NewValue;
                ValueChanging?.Invoke(this, e.NewValue);
            }
        }

        // Event to notify when user changes the slider value
        public event EventHandler<double> ValueChanging;
        public event EventHandler<double> RangeStartChanging; // New event
        public event EventHandler<double> RangeEndChanging;   // New event

        private void Thumb_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = true;
            (sender as UIElement).CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void Thumb_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            (sender as UIElement).ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void StartThumb_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                UpdateThumbPosition(e, true);
                e.Handled = true;
            }
        }

        private void EndThumb_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                UpdateThumbPosition(e, false);
                e.Handled = true;
            }
        }

        private void UpdateThumbPosition(PointerRoutedEventArgs e, bool isStart)
        {
            var pos = e.GetCurrentPoint(this).Position.X;
            double width = ActualWidth - 20;
            double range = Maximum - Minimum;
            
            double newVal = ((pos - 10) / width) * range + Minimum;
            if (newVal < Minimum) newVal = Minimum;
            if (newVal > Maximum) newVal = Maximum;

            if (isStart)
            {
                if (newVal > RangeEnd) newVal = RangeEnd;
                RangeStart = newVal;
                RangeStartChanging?.Invoke(this, RangeStart); // Invoke new event
            }
            else
            {
                if (newVal < RangeStart) newVal = RangeStart;
                RangeEnd = newVal;
                RangeEndChanging?.Invoke(this, RangeEnd); // Invoke new event
            }
            UpdateVisuals();
        }
    }
}
