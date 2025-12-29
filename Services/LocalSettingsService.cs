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
                    // 處理陣列類型的特殊情況 - 使用逗號分隔的字串
                    if (typeof(T) == typeof(string[]))
                    {
                        if (value is string pathsString)
                        {
                            // 新格式：逗號分隔的字串
                            if (string.IsNullOrEmpty(pathsString))
                                return (T)(object)new string[0];
                            
                            var paths = pathsString.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                            return (T)(object)paths;
                        }
                        else if (value is System.Collections.IList list)
                        {
                            // 兼容舊格式（IList）
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
                    
                    // 嘗試型別轉換
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        // 型別轉換失敗，返回預設值
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
                // 處理陣列類型的特殊情況 - 使用逗號分隔的字串
                if (value is string[] stringArray)
                {
                    // 使用 "|||" 作為分隔符（避免路徑中包含逗號的問題）
                    var pathsString = string.Join("|||", stringArray);
                    
                    // 檢查字串長度，避免超過 Windows 應用程式資料容器的限制（通常限制為 8KB）
                    const int maxStringLength = 8000; // 留一些餘量
                    if (pathsString.Length > maxStringLength)
                    {
                        System.Diagnostics.Debug.WriteLine($"LocalSettingsService: Paths string too long ({pathsString.Length} chars), truncating...");
                        // 如果字串太長，截斷陣列，只保留前面的項
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
                    // 對於字串型別，檢查長度
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
                // 不拋出異常，避免程式崩潰
            }
            catch (InvalidCastException ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveSetting InvalidCastException for key '{key}': {ex.Message}");
                // 不拋出異常，避免程式崩潰
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.SaveSetting error for key '{key}': {ex.Message}");
                // 不拋出異常，避免程式崩潰
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

        // History paths (最多儲存50條)
        public static List<string> HistoryPaths
        {
            get
            {
                var paths = GetSetting<string[]>(KeyHistoryPaths, null);
                return paths?.ToList() ?? new List<string>();
            }
            set
            {
                // 限制最多50條
                var pathsToSave = value?.Take(50).ToArray() ?? new string[0];
                SaveSetting(KeyHistoryPaths, pathsToSave);
            }
        }

        // Playlist paths (播放清單路徑)
        public static List<string> PlaylistPaths
        {
            get
            {
                var paths = GetSetting<string[]>(KeyPlaylistPaths, null);
                return paths?.ToList() ?? new List<string>();
            }
            set
            {
                // 限制最多200條（播放清單可能比歷史記錄更多）
                var pathsToSave = value?.Take(200).ToArray() ?? new string[0];
                SaveSetting(KeyPlaylistPaths, pathsToSave);
            }
        }

        // 新增歷史記錄路徑（自動去重並限制數量）
        public static void AddHistoryPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;

                var history = HistoryPaths;
                // 如果已存在，先移除
                history.Remove(path);
                // 新增到最前面
                history.Insert(0, path);
                // 限制最多50條
                if (history.Count > 50)
                {
                    history = history.Take(50).ToList();
                }
                HistoryPaths = history;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettingsService.AddHistoryPath error for path '{path}': {ex.Message}");
                // 不拋出異常，避免程式崩潰
                // 歷史記錄儲存失敗不應該影響媒體播放
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
