using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Collections.Generic;
using Windows.Foundation;

namespace FlowerPlayer.Controls
{
    public sealed partial class WaveformControl : UserControl
    {
        public static readonly DependencyProperty WaveformDataProperty = DependencyProperty.Register("WaveformData", typeof(float[]), typeof(WaveformControl), new PropertyMetadata(null, OnWaveformDataChanged));

        public float[] WaveformData { get => (float[])GetValue(WaveformDataProperty); set => SetValue(WaveformDataProperty, value); }

        public WaveformControl()
        {
            this.InitializeComponent();
        }

        private static void OnWaveformDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as WaveformControl)?.RenderWaveform();
        }

        private void RenderWaveform()
        {
            if (WaveformData == null || WaveformData.Length == 0)
            {
                WavePolyline.Points.Clear();
                return;
            }

            var points = new PointCollection();
            double width = ActualWidth > 0 ? ActualWidth : 800;
            double height = ActualHeight > 0 ? ActualHeight : 60;
            double mid = height / 2;

            // Simple rendering: Map index to X, value to Y
            // We need to downsample or upsample to fit width
            
            int step = WaveformData.Length / (int)width;
            if (step < 1) step = 1;

            for (int i = 0; i < WaveformData.Length; i += step)
            {
                double x = (double)i / WaveformData.Length * width;
                double y = mid + (WaveformData[i] * mid);
                points.Add(new Point(x, y));
            }

            WavePolyline.Points = points;
        }
    }
}
