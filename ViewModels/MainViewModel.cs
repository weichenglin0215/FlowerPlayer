using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerPlayer.Services;
using System;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using FlowerPlayer.Helpers;

namespace FlowerPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IMediaService _mediaService;

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

        [ObservableProperty]
        private bool _isSmartSkipActive;

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

        public async void GenerateWaveform(StorageFile file)
        {
            WaveformData = await MediaHelper.GenerateWaveformAsync(file, 1000);
        }

        partial void OnRangeStartChanged(double value) => UpdateRange();
        partial void OnRangeEndChanged(double value) => UpdateRange();
        partial void OnIsLoopingChanged(bool value) => _mediaService.IsLooping = value;
        
        // Update setting when property changes
        partial void OnIsWaveformVisibleChanged(bool value) => LocalSettingsService.IsWaveformVisible = value;

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
        public void Stop() => _mediaService.Stop();

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
