using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Collections.Generic;
using Windows.Foundation;
using System;

namespace FlowerPlayer.Controls
{
    public sealed partial class WaveformControl : UserControl
    {
        public static readonly DependencyProperty WaveformDataProperty = DependencyProperty.Register("WaveformData", typeof(float[]), typeof(WaveformControl), new PropertyMetadata(null, OnWaveformDataChanged));

        public float[] WaveformData { get => (float[])GetValue(WaveformDataProperty); set => SetValue(WaveformDataProperty, value); }

        public WaveformControl()
        {
            this.InitializeComponent();
            this.SizeChanged += WaveformControl_SizeChanged;
        }

        private void WaveformControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderWaveform();
        }

        private static void OnWaveformDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as WaveformControl;
            if (control != null)
            {
                System.Diagnostics.Debug.WriteLine($"WaveformControl.OnWaveformDataChanged: Called, old value={(e.OldValue as float[])?.Length ?? 0}, new value={(e.NewValue as float[])?.Length ?? 0}");
                control.RenderWaveform();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"WaveformControl.OnWaveformDataChanged: control is null!");
            }
        }

        private void RenderWaveform()
        {
            System.Diagnostics.Debug.WriteLine($"WaveformControl.RenderWaveform: Called, WaveformData={(WaveformData != null ? $"Length={WaveformData.Length}" : "null")}, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}");
            
            if (WaveformData == null || WaveformData.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("WaveformControl.RenderWaveform: No data, clearing points");
                WavePolyline.Points.Clear();
                return;
            }

            var points = new PointCollection();
            double width = ActualWidth > 0 ? ActualWidth : 800;
            double height = ActualHeight > 0 ? ActualHeight : 60;
            double mid = height / 2;

            System.Diagnostics.Debug.WriteLine($"WaveformControl.RenderWaveform: width={width}, height={height}, mid={mid}");

            // 計算波形的最大值和最小值，用於正規化
            float maxValue = 0;
            foreach (var value in WaveformData)
            {
                float absValue = Math.Abs(value);
                if (absValue > maxValue) maxValue = absValue;
            }

            System.Diagnostics.Debug.WriteLine($"WaveformControl.RenderWaveform: maxValue={maxValue}");

            // 如果最大值為0，使用默認值
            if (maxValue == 0)
            {
                System.Diagnostics.Debug.WriteLine("WaveformControl.RenderWaveform: WARNING - maxValue is 0, using default 1.0");
                maxValue = 1.0f;
            }

            // Simple rendering: Map index to X, value to Y
            // We need to downsample or upsample to fit width
            
            int step = WaveformData.Length / (int)width;
            if (step < 1) step = 1;

            System.Diagnostics.Debug.WriteLine($"WaveformControl.RenderWaveform: step={step}, will generate ~{WaveformData.Length / step} points");

            for (int i = 0; i < WaveformData.Length; i += step)
            {
                double x = (double)i / WaveformData.Length * width;
                
                // 正規化值到 -1 到 1 範圍，然後放大到可視高度
                float normalizedValue = WaveformData[i] / maxValue;
                
                // 使用更大的放大係數，讓波形更明顯（使用高度的80%）
                double amplitude = height * 0.4; // 40% 的高度用於上下振幅
                // 上下顛倒：低處是小聲，高處是大聲
                double y = mid - (normalizedValue * amplitude);
                
                // 確保 y 在有效範圍內
                if (y < 0) y = 0;
                if (y > height) y = height;
                
                points.Add(new Point(x, y));
            }

            WavePolyline.Points = points;
            System.Diagnostics.Debug.WriteLine($"WaveformControl.RenderWaveform: Generated {points.Count} points");
        }
    }
}
