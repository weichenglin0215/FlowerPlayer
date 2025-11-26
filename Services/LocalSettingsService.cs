using System;
using System.Collections.Generic;
using System.Linq;
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
                // 处理数组类型的特殊情况 - 使用逗号分隔的字符串
                if (typeof(T) == typeof(string[]))
                {
                    if (value is string pathsString)
                    {
                        // 新格式：逗号分隔的字符串
                        if (string.IsNullOrEmpty(pathsString))
                            return (T)(object)new string[0];
                        
                        var paths = pathsString.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                        return (T)(object)paths;
                    }
                    else if (value is System.Collections.IList list)
                    {
                        // 兼容旧格式（IList）
                        var stringArray = new string[list.Count];
                        for (int i = 0; i < list.Count; i++)
                        {
                            stringArray[i] = list[i]?.ToString() ?? string.Empty;
                        }
                        return (T)(object)stringArray;
                    }
                    return defaultValue;
                }
                return (T)value;
            }
            return defaultValue;
        }

        public static void SaveSetting<T>(string key, T value)
        {
            // 处理数组类型的特殊情况 - 使用逗号分隔的字符串
            if (value is string[] stringArray)
            {
                // 使用 "|||" 作为分隔符（避免路径中包含逗号的问题）
                var pathsString = string.Join("|||", stringArray);
                _localSettings.Values[key] = pathsString;
            }
            else
            {
                _localSettings.Values[key] = value;
            }
            SettingChanged?.Invoke(null, key);
        }

        // Defined Keys
        public const string KeyAutoPlayOnOpen = "AutoPlayOnOpen";
        public const string KeyResumeLastFile = "ResumeLastFile";
        public const string KeySmartSkipPlayDuration = "SmartSkipPlayDuration";
        public const string KeySmartSkipSkipDuration = "SmartSkipSkipDuration";
        public const string KeyIsWaveformVisible = "IsWaveformVisible";
        public const string KeyLastFilePath = "LastFilePath";
        public const string KeyHistoryPaths = "HistoryPaths";
        public const string KeyAutoPlayNext = "AutoPlayNext";

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

        public static bool AutoPlayNext
        {
            get => GetSetting(KeyAutoPlayNext, false);
            set => SaveSetting(KeyAutoPlayNext, value);
        }

        // History paths (最多保存50条)
        public static List<string> HistoryPaths
        {
            get
            {
                var paths = GetSetting<string[]>(KeyHistoryPaths, null);
                return paths?.ToList() ?? new List<string>();
            }
            set
            {
                // 限制最多50条
                var pathsToSave = value?.Take(50).ToArray() ?? new string[0];
                SaveSetting(KeyHistoryPaths, pathsToSave);
            }
        }

        // 添加历史记录路径（自动去重并限制数量）
        public static void AddHistoryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var history = HistoryPaths;
            // 如果已存在，先移除
            history.Remove(path);
            // 添加到最前面
            history.Insert(0, path);
            // 限制最多50条
            if (history.Count > 50)
            {
                history = history.Take(50).ToList();
            }
            HistoryPaths = history;
        }
    }
}
