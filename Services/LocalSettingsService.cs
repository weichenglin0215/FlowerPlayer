using System;
using Windows.Storage;

namespace FlowerPlayer.Services
{
    public static class LocalSettingsService
    {
        private static ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public static event EventHandler<string> SettingChanged;

        public static T GetSetting<T>(string key, T defaultValue)
        {
            if (_localSettings.Values.TryGetValue(key, out var value))
            {
                return (T)value;
            }
            return defaultValue;
        }

        public static void SaveSetting<T>(string key, T value)
        {
            _localSettings.Values[key] = value;
            SettingChanged?.Invoke(null, key);
        }

        // Defined Keys
        public const string KeyAutoPlayOnOpen = "AutoPlayOnOpen";
        public const string KeyResumeLastFile = "ResumeLastFile";
        public const string KeySmartSkipPlayDuration = "SmartSkipPlayDuration";
        public const string KeySmartSkipSkipDuration = "SmartSkipSkipDuration";
        public const string KeyIsWaveformVisible = "IsWaveformVisible";
        public const string KeyLastFilePath = "LastFilePath";

        // Default Values
        public static bool AutoPlayOnOpen
        {
            get => GetSetting(KeyAutoPlayOnOpen, true);
            set => SaveSetting(KeyAutoPlayOnOpen, value);
        }

        public static bool ResumeLastFile
        {
            get => GetSetting(KeyResumeLastFile, false);
            set => SaveSetting(KeyResumeLastFile, value);
        }

        public static string LastFilePath
        {
            get => GetSetting(KeyLastFilePath, string.Empty);
            set => SaveSetting(KeyLastFilePath, value);
        }

        public static double SmartSkipPlayDuration
        {
            get => GetSetting(KeySmartSkipPlayDuration, 5.0);
            set => SaveSetting(KeySmartSkipPlayDuration, value);
        }

        public static double SmartSkipSkipDuration
        {
            get => GetSetting(KeySmartSkipSkipDuration, 30.0);
            set => SaveSetting(KeySmartSkipSkipDuration, value);
        }

        public static bool IsWaveformVisible
        {
            get => GetSetting(KeyIsWaveformVisible, false);
            set => SaveSetting(KeyIsWaveformVisible, value);
        }
    }
}
