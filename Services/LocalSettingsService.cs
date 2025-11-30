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
            try
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
                    
                    // 安全的类型转换
                    if (value is T directValue)
                    {
                        return directValue;
                    }
                    
                    // 尝试类型转换
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        // 类型转换失败，返回默认值
                        System.Diagnostics.Debug.WriteLine($"LocalSettingsService: Failed to convert value for key '{key}' from {value?.GetType().Name} to {typeof(T).Name}");
                        return defaultValue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.GetSetting error for key '{key}': {ex.Message}");
            }
            return defaultValue;
        }

        public static void SaveSetting<T>(string key, T value)
        {
            try
            {
                // 处理数组类型的特殊情况 - 使用逗号分隔的字符串
                if (value is string[] stringArray)
                {
                    // 使用 "|||" 作为分隔符（避免路径中包含逗号的问题）
                    var pathsString = string.Join("|||", stringArray);
                    
                    // 检查字符串长度，避免超过 Windows 应用程序数据容器的限制（通常限制为 8KB）
                    const int maxStringLength = 8000; // 留一些余量
                    if (pathsString.Length > maxStringLength)
                    {
                        System.Diagnostics.Debug.WriteLine($"LocalSettingsService: Paths string too long ({pathsString.Length} chars), truncating...");
                        // 如果字符串太长，截断数组，只保留前面的项
                        int maxItems = 1;
                        while (maxItems < stringArray.Length)
                        {
                            var testString = string.Join("|||", stringArray.Take(maxItems + 1));
                            if (testString.Length > maxStringLength)
                                break;
                            maxItems++;
                        }
                        pathsString = string.Join("|||", stringArray.Take(maxItems));
                        System.Diagnostics.Debug.WriteLine($"LocalSettingsService: Reduced to {maxItems} items");
                    }
                    
                    _localSettings.Values[key] = pathsString;
                }
                else
                {
                    // 对于字符串类型，检查长度
                    if (value is string strValue && strValue.Length > 8000)
                    {
                        System.Diagnostics.Debug.WriteLine($"LocalSettingsService: String value too long ({strValue.Length} chars) for key '{key}', truncating...");
                        _localSettings.Values[key] = strValue.Substring(0, 8000);
                    }
                    else
                    {
                        _localSettings.Values[key] = value;
                    }
                }
                
                SettingChanged?.Invoke(null, key);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveSetting COMException for key '{key}': {ex.Message}");
                // 不抛出异常，避免程序崩溃
            }
            catch (InvalidCastException ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveSetting InvalidCastException for key '{key}': {ex.Message}");
                // 不抛出异常，避免程序崩溃
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveSetting error for key '{key}': {ex.Message}");
                // 不抛出异常，避免程序崩溃
            }
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
        public const string KeySkipStartSeconds = "SkipStartSeconds";
        public const string KeyPlaylistPaths = "PlaylistPaths";
        
        // Window position and size keys
        public const string KeyMainWindowPosition = "MainWindowPosition";
        public const string KeyMainWindowSize = "MainWindowSize";
        public const string KeyPlaylistWindowPosition = "PlaylistWindowPosition";
        public const string KeyPlaylistWindowSize = "PlaylistWindowSize";
        public const string KeyHistoryWindowPosition = "HistoryWindowPosition";
        public const string KeyHistoryWindowSize = "HistoryWindowSize";
        public const string KeySettingsWindowPosition = "SettingsWindowPosition";
        public const string KeySettingsWindowSize = "SettingsWindowSize";

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

        public static double SkipStartSeconds
        {
            get => GetSetting(KeySkipStartSeconds, 0.0);
            set => SaveSetting(KeySkipStartSeconds, value);
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

        // Playlist paths (播放清单路径)
        public static List<string> PlaylistPaths
        {
            get
            {
                var paths = GetSetting<string[]>(KeyPlaylistPaths, null);
                return paths?.ToList() ?? new List<string>();
            }
            set
            {
                // 限制最多200条（播放清单可能比历史记录更多）
                var pathsToSave = value?.Take(200).ToArray() ?? new string[0];
                SaveSetting(KeyPlaylistPaths, pathsToSave);
            }
        }

        // 添加历史记录路径（自动去重并限制数量）
        public static void AddHistoryPath(string path)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.AddHistoryPath error for path '{path}': {ex.Message}");
                // 不抛出异常，避免程序崩溃
                // 历史记录保存失败不应该影响媒体播放
            }
        }
        
        // Window position and size helpers
        public static Windows.Graphics.PointInt32? GetWindowPosition(string key)
        {
            try
            {
                var value = GetSetting<string>(key, null);
                if (string.IsNullOrEmpty(value)) return null;
                var parts = value.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    return new Windows.Graphics.PointInt32(x, y);
                }
            }
            catch { }
            return null;
        }
        
        public static void SaveWindowPosition(string key, Windows.Graphics.PointInt32 position)
        {
            try
            {
                SaveSetting(key, $"{position.X},{position.Y}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveWindowPosition error for key '{key}': {ex.Message}");
            }
        }
        
        public static Windows.Graphics.SizeInt32? GetWindowSize(string key)
        {
            try
            {
                var value = GetSetting<string>(key, null);
                if (string.IsNullOrEmpty(value)) return null;
                var parts = value.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                {
                    return new Windows.Graphics.SizeInt32(width, height);
                }
            }
            catch { }
            return null;
        }
        
        public static void SaveWindowSize(string key, Windows.Graphics.SizeInt32 size)
        {
            try
            {
                SaveSetting(key, $"{size.Width},{size.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveWindowSize error for key '{key}': {ex.Message}");
            }
        }
    }
}
