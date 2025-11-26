using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerPlayer.Services;
using System;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using FlowerPlayer.Helpers;
using Microsoft.UI.Xaml;

namespace FlowerPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IMediaService _mediaService;
        public Microsoft.UI.Xaml.XamlRoot XamlRoot { get; set; }
        
        // 存储已生成的波形数据，避免重复生成
        private readonly System.Collections.Generic.Dictionary<string, float[]> _waveformCache = new System.Collections.Generic.Dictionary<string, float[]>();
        
        // 当前正在生成波形的文件路径
        private string _currentGeneratingFilePath = null;

        [ObservableProperty]
        private string _windowTitle = "FlowerPlayer";

        [ObservableProperty]
        private TimeSpan _currentTime;

        [ObservableProperty]
        private TimeSpan _totalDuration;

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private double _volume = 1.0;
        
        [ObservableProperty]
        private bool _isWaveformVisible;
        
        [ObservableProperty]
        private bool _isGeneratingWaveform;
        
        partial void OnIsGeneratingWaveformChanged(bool value)
        {
            // 當生成狀態改變時，通知 IsFileLoadedAndNotGeneratingWaveform 屬性變化
            OnPropertyChanged(nameof(IsFileLoadedAndNotGeneratingWaveform));
        }
        
        partial void OnIsFileLoadedChanged(bool value)
        {
            // 當文件載入狀態改變時，通知 IsFileLoadedAndNotGeneratingWaveform 屬性變化
            OnPropertyChanged(nameof(IsFileLoadedAndNotGeneratingWaveform));
        }

        [ObservableProperty]
        private double _rangeStart;

        [ObservableProperty]
        private double _rangeEnd;

        [ObservableProperty]
        private bool _isLooping;

        [ObservableProperty]
        private float[] _waveformData;

        [ObservableProperty]
        private double _sliderPosition;

        [ObservableProperty]
        private bool _hasVideo;

        [ObservableProperty]
        private bool _isFileLoaded;
        
        // 計算屬性：文件已載入且不在生成波形
        public bool IsFileLoadedAndNotGeneratingWaveform => IsFileLoaded && !IsGeneratingWaveform;

        [ObservableProperty]
        private bool _isSmartSkipActive;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private string _currentFileName;

        [ObservableProperty]
        private string _currentFileDirectory;

        private TimeSpan _smartSkipSegmentStart;

        public IMediaService MediaService => _mediaService;
        
        public MainViewModel()
        {
            // In a real app, use DI. Here we manually instantiate for simplicity if DI isn't set up yet.
            _mediaService = new MediaService();
            
            // Load settings
            _isWaveformVisible = LocalSettingsService.IsWaveformVisible;
            
            // 監聽位置變化以處理快速跳播邏輯
            _mediaService.PositionChanged += MediaService_PositionChanged;
        }

        private void MediaService_PositionChanged(object sender, TimeSpan position)
        {
            if (_isSmartSkipActive && _mediaService.CurrentState == MediaState.Playing)
            {
                double playDuration = LocalSettingsService.SmartSkipPlayDuration;
                double skipDuration = LocalSettingsService.SmartSkipSkipDuration;

                // 如果當前播放位置與片段開始位置相差超過設定的播放時間
                if ((position - _smartSkipSegmentStart).TotalSeconds >= playDuration)
                {
                    // 跳轉設定的跳播時間
                    _mediaService.Position += TimeSpan.FromSeconds(skipDuration);
                    // 更新片段開始位置
                    _smartSkipSegmentStart = _mediaService.Position;
                }
            }
        }

        public async System.Threading.Tasks.Task GenerateWaveformAsync(StorageFile file, bool firstFiveMinutesOnly = false)
        {
            if (file == null) return;
            
            string filePath = file.Path;
            
            // 检查是否已经生成过波形
            if (_waveformCache.ContainsKey(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Using cached waveform for {file.Name}");
                WaveformData = _waveformCache[filePath];
                IsGeneratingWaveform = false;
                return;
            }
            
            // 检查是否正在生成
            if (_currentGeneratingFilePath == filePath)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Already generating waveform for {file.Name}");
                return;
            }
            
            try
            {
                IsGeneratingWaveform = true;
                _currentGeneratingFilePath = filePath;
                
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Starting for {file.Name}, firstFiveMinutesOnly={firstFiveMinutesOnly}");
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Calling MediaHelper.GenerateWaveformAsync...");
                
                var waveform = await MediaHelper.GenerateWaveformAsync(file, 1000, firstFiveMinutesOnly);
                
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Received waveform, length={waveform?.Length ?? 0}");
                
                if (waveform == null)
                {
                    System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: ERROR - waveform is null!");
                    IsGeneratingWaveform = false;
                    _currentGeneratingFilePath = null;
                    return;
                }
                
                // 缓存波形数据
                _waveformCache[filePath] = waveform;
                
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Setting WaveformData property...");
                WaveformData = waveform;
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: WaveformData set, current value length={WaveformData?.Length ?? 0}");
                
                if (waveform.Length > 0)
                {
                    float maxValue = waveform[0];
                    float minValue = waveform[0];
                    for (int i = 1; i < waveform.Length; i++)
                    {
                        if (waveform[i] > maxValue) maxValue = waveform[i];
                        if (waveform[i] < minValue) minValue = waveform[i];
                    }
                    var first5 = waveform.Length >= 5 ? waveform.Take(5).ToArray() : waveform;
                    System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Waveform data - Max: {maxValue}, Min: {minValue}, First 5: [{string.Join(", ", first5.Select(v => v.ToString("F3")))}]");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: WARNING - Waveform data is empty!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Error - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: Error type - {ex.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: StackTrace - {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"MainViewModel.GenerateWaveformAsync: InnerException - {ex.InnerException.Message}");
                }
            }
            finally
            {
                IsGeneratingWaveform = false;
                _currentGeneratingFilePath = null;
            }
        }

        partial void OnRangeStartChanged(double value) => UpdateRange();
        partial void OnRangeEndChanged(double value) => UpdateRange();
        partial void OnIsLoopingChanged(bool value) => _mediaService.IsLooping = value;
        
        // Update setting when property changes
        partial void OnIsWaveformVisibleChanged(bool value)
        {
            LocalSettingsService.IsWaveformVisible = value;
            
            // 當關閉波形顯示時，不清空波形數據（保留在緩存中），只是隱藏
            // 當開啟顯示波形時，檢查是否已有緩存的波形數據
            if (value && _mediaService.CurrentFile != null)
            {
                string filePath = _mediaService.CurrentFile.Path;
                
                // 如果已經有緩存的波形數據，直接顯示
                if (_waveformCache.ContainsKey(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"MainViewModel.OnIsWaveformVisibleChanged: Using cached waveform for {_mediaService.CurrentFile.Name}");
                    WaveformData = _waveformCache[filePath];
                    return;
                }
                
                // 如果沒有緩存，異步檢查是否需要確認
                _ = CheckAndGenerateWaveformAsync();
            }
        }
        
        private async System.Threading.Tasks.Task CheckAndGenerateWaveformAsync()
        {
            if (_mediaService.CurrentFile == null) return;
            
            try
            {
                // 獲取媒體文件時長
                var audioProperties = await _mediaService.CurrentFile.Properties.GetMusicPropertiesAsync();
                var duration = audioProperties.Duration;
                
                // 如果超過一分鐘，顯示確認對話框
                if (duration.TotalMinutes > 1.0)
                {
                    if (XamlRoot == null)
                    {
                        System.Diagnostics.Debug.WriteLine("MainViewModel.CheckAndGenerateWaveformAsync: XamlRoot is null, cannot show dialog");
                        await GenerateWaveformAsync(_mediaService.CurrentFile, false);
                        return;
                    }
                    
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "確認",
                        Content = $"媒體檔案超過一分鐘（{duration.TotalMinutes:F1} 分鐘），產生波形圖需要較長時間，請選擇：",
                        PrimaryButtonText = "顯示整段波形圖",
                        SecondaryButtonText = "取消",
                        CloseButtonText = "只顯示前五分鐘",
                        XamlRoot = XamlRoot
                    };
                    
                    var result = await dialog.ShowAsync();
                    
                    if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
                    {
                        // 用戶取消，恢復未啟動狀態
                        IsWaveformVisible = false;
                        return;
                    }
                    else if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
                    {
                        // 用戶選擇"只顯示前五分鐘"
                        await GenerateWaveformAsync(_mediaService.CurrentFile, true);
                    }
                    else
                    {
                        // 用戶選擇"顯示整段波形圖"
                        await GenerateWaveformAsync(_mediaService.CurrentFile, false);
                    }
                }
                else
                {
                    // 不超過一分鐘，直接生成
                    await GenerateWaveformAsync(_mediaService.CurrentFile, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel.CheckAndGenerateWaveformAsync: Error checking duration: {ex.Message}");
                // 如果獲取時長失敗，仍然嘗試生成波形
                if (_mediaService.CurrentFile != null)
                {
                    await GenerateWaveformAsync(_mediaService.CurrentFile, false);
                }
            }
        }

        private void UpdateRange()
        {
            if (RangeEnd > RangeStart)
            {
                _mediaService.SetRange(TimeSpan.FromSeconds(RangeStart), TimeSpan.FromSeconds(RangeEnd));
            }
        }

        [RelayCommand]
        public async Task OpenFile()
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");
            
            // WinUI 3 requires Window Handle for Pickers
            // We need to pass it from the View or use a service. 
            // For now, we'll assume the View handles the picker or we use a workaround.
            // But wait, we can't easily get HWND here without passing it.
            // Let's make this command receive the HWND or handle it in CodeBehind for now, 
            // OR use the InitializeWithWindow helper if we had it.
            // I will leave the implementation empty here and handle it in View's code behind 
            // calling a method on VM with the file.
        }

        public void OpenFile(StorageFile file)
        {
            _mediaService.Open(file);
            
            // Save last file path
            LocalSettingsService.LastFilePath = file.Path;

            // Auto Play
            if (LocalSettingsService.AutoPlayOnOpen)
            {
                _mediaService.Play();
            }
        }

        [RelayCommand]
        public void PlayPause()
        {
            if (IsPlaying) _mediaService.Pause();
            else _mediaService.Play();
        }

        [RelayCommand]
        public void Stop()
        {
            _mediaService.Stop();
            // 清除文件名和目录信息
            CurrentFileName = string.Empty;
            CurrentFileDirectory = string.Empty;
        }

        [RelayCommand]
        public void SeekForward() => _mediaService.Position += TimeSpan.FromMinutes(1);

        [RelayCommand]
        public void SeekBackward() => _mediaService.Position -= TimeSpan.FromMinutes(1);

        [RelayCommand]
        public void StepNextFrame() => _mediaService.StepForward();

        [RelayCommand]
        public void StepPrevFrame() => _mediaService.StepBackward();

        [RelayCommand]
        public void ToggleWaveform() => IsWaveformVisible = !IsWaveformVisible;

        [RelayCommand]
        public void SetRangeStart()
        {
             RangeStart = CurrentTime.TotalSeconds;
             // Ensure range is valid
             if (RangeEnd < RangeStart) RangeEnd = RangeStart;
        }

        [RelayCommand]
        public void SetRangeEnd()
        {
             RangeEnd = CurrentTime.TotalSeconds;
             // Ensure range is valid
             if (RangeStart > RangeEnd) RangeStart = RangeEnd;
        }
        [RelayCommand]
        public void ToggleSmartSkip()
        {
            IsSmartSkipActive = !IsSmartSkipActive;
        }

        [ObservableProperty]
        private bool _isMuted;

        partial void OnIsSmartSkipActiveChanged(bool value)
        {
            if (value)
            {
                _smartSkipSegmentStart = _mediaService.Position;
                if (!IsPlaying)
                {
                    _mediaService.Play();
                }
            }
        }

        partial void OnVolumeChanged(double value) => _mediaService.Volume = value;
        partial void OnIsMutedChanged(bool value)
        {
            _mediaService.IsMuted = value;
            OnPropertyChanged(nameof(VolumeIcon));
        }

        public string VolumeIcon => IsMuted ? "\uE74F" : "\uE767";

        [RelayCommand]
        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }
    }
}
