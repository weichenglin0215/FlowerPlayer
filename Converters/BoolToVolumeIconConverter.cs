using Microsoft.UI.Xaml.Data;
using System;
using Microsoft.UI.Xaml.Controls;

namespace FlowerPlayer.Converters
{
    public class BoolToVolumeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isMuted = (bool)value;
            // Segoe MDL2 Assets
            // Volume: \uE767
            // Mute: \uE74F
            return isMuted ? "\uE74F" : "\uE767";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
