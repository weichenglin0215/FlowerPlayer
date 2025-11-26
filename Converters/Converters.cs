using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace FlowerPlayer.Converters
{
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    public class TimeConverter : IValueConverter
    {
        // Default frame rate if not specified. Ideally this should be dynamic.
        private double _frameRate = 30.0;

        public double FrameRate
        {
            get => _frameRate;
            set => _frameRate = value;
        }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            TimeSpan timeSpan;
            if (value is TimeSpan ts)
                timeSpan = ts;
            else if (value is double d)
                timeSpan = TimeSpan.FromSeconds(d);
            else
                return "00:00:00:00";

            // Snap time to the nearest frame grid to avoid rounding artifacts (like duplicate .01)
            // This logic aligns seconds and frames consistently.
            
            long fps = (long)Math.Round(_frameRate);
            if (fps <= 0) fps = 30;

            long totalFrames = (long)Math.Round(timeSpan.TotalSeconds * _frameRate);
            
            // Re-calculate seconds based on total frames
            long totalSeconds = totalFrames / fps;
            long currentFrame = totalFrames % fps;
            
            TimeSpan derivedTime = TimeSpan.FromSeconds(totalSeconds);
            int displayFrame = (int)currentFrame + 1; // 1-based indexing

            return $"{derivedTime.Hours:D2}:{derivedTime.Minutes:D2}:{derivedTime.Seconds:D2}.{displayFrame:D2}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class PlayPauseLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? "暫停" : "播放";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
