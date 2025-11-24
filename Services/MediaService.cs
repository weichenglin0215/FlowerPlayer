using System;
using System.Linq;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.Media.Core;
using Windows.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Media.Editing;
using System.Threading.Tasks;

namespace FlowerPlayer.Services
{
    public class MediaService : ObservableObject, IMediaService
    {
        private readonly MediaPlayer _player;
        private TimeSpan? _rangeStart;
        private TimeSpan? _rangeEnd;
        private bool _isLooping;
        private StorageFile _currentFile;
        private bool _hasVideo;

        public event EventHandler<TimeSpan> PositionChanged;
        public event EventHandler<MediaState> StateChanged;
        public event EventHandler<StorageFile> MediaOpened;
        public event EventHandler<TimeSpan> DurationChanged;

        public MediaPlayer Player => _player;

        public MediaService()
        {
            _player = new MediaPlayer();
            _player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
            _player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            _player.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;
        }

        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

        public TimeSpan Position
        {
            get => _player.PlaybackSession.Position;
            set => _player.PlaybackSession.Position = value;
        }

        public MediaState CurrentState
        {
            get
            {
                switch (_player.PlaybackSession.PlaybackState)
                {
                    case MediaPlaybackState.Playing: return MediaState.Playing;
                    case MediaPlaybackState.Paused: return MediaState.Paused;
                    default: return MediaState.Stopped;
                }
            }
        }

        public double Volume
        {
            get => _player.Volume;
            set => _player.Volume = value;
        }

        public bool IsMuted
        {
            get => _player.IsMuted;
            set => _player.IsMuted = value;
        }

        public bool IsLooping
        {
            get => _isLooping;
            set
            {
                _isLooping = value;
                _player.IsLoopingEnabled = value && _rangeStart == null; // Native loop if no range
            }
        }

        public bool HasVideo => _hasVideo;

        public async Task<double> GetFrameRateAsync()
        {
             if (_currentFile == null) return 30.0;
             try
             {
                 // Use MediaClip to get video properties, as StorageFile.Properties.GetVideoPropertiesAsync 
                 // might not return the correct object type with FrameRate in WinUI 3 context or namespace conflict.
                 // Alternatively, accessing the properties via KnownProperties key might be safer.
                 // But simply, VideoProperties in UWP/WinUI 3 usually has Duration, Width, Height etc.
                 // FrameRate is not directly on VideoProperties in some SDK versions or requires specific casting.
                 // Let's use MediaEncodingProfile from MediaSource if possible, or fallback to simple calculation.
                 
                 // Actually, System.Video.FrameRate is a property we can retrieve via RetrievePropertiesAsync
                 var props = await _currentFile.Properties.RetrievePropertiesAsync(new[] { "System.Video.FrameRate" });
                 if (props.TryGetValue("System.Video.FrameRate", out var rateObj))
                 {
                     // The property is usually returned as numerator/denominator (100ns units) or just raw value.
                     // The documentation says System.Video.FrameRate is "The frame rate of the video, in units of 1000 frames per second."
                     // So 30000 means 30fps.
                     if (rateObj is uint rate)
                     {
                         return rate / 1000.0;
                     }
                 }
             }
             catch { }
             return 30.0; // Fallback
        }

        public void Open(StorageFile file)
        {
            _currentFile = file;
            
            // 檢測是否為影片檔案（根據副檔名）
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
            _hasVideo = videoExtensions.Contains(file.FileType.ToLower());
            
            _player.Source = MediaSource.CreateFromStorageFile(file);
            MediaOpened?.Invoke(this, file);
        }

        public void Play() => _player.Play();
        public void Pause() => _player.Pause();
        public void Stop()
        {
            _player.Pause();
            _player.PlaybackSession.Position = TimeSpan.Zero;
        }

        public void StepForward(int frames = 1)
        {
            // MediaPlayer doesn't have direct "Step" API, but we can seek.
            // Assuming 30fps for now, or use MediaHelper to estimate.
            // Ideally we use StepForwardOneFrame() if available in newer SDKs or simulate it.
            // WinUI 3 MediaPlayer has StepForwardOneFrame()!
            
            if (_player.PlaybackSession.CanSeek)
            {
                for(int i=0; i<frames; i++)
                    _player.StepForwardOneFrame();
            }
        }

        public void StepBackward(int frames = 1)
        {
             if (_player.PlaybackSession.CanSeek)
            {
                for(int i=0; i<frames; i++)
                    _player.StepBackwardOneFrame();
            }
        }

        public void SetRange(TimeSpan start, TimeSpan end)
        {
            _rangeStart = start;
            _rangeEnd = end;
        }

        public void ClearRange()
        {
            _rangeStart = null;
            _rangeEnd = null;
            _player.IsLoopingEnabled = _isLooping;
        }

        public async Task SaveRangeAsAsync(StorageFile destination)
        {
            if (_currentFile == null || _rangeStart == null || _rangeEnd == null) return;

            var clip = await MediaClip.CreateFromFileAsync(_currentFile);
            clip.TrimTimeFromStart = _rangeStart.Value;
            clip.TrimTimeFromEnd = clip.OriginalDuration - _rangeEnd.Value;

            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            await composition.RenderToFileAsync(destination);
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            StateChanged?.Invoke(this, CurrentState);
        }

        private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
             DurationChanged?.Invoke(this, Duration);
        }

        private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            PositionChanged?.Invoke(this, sender.Position);

            // Handle Range Looping
            if (_isLooping && _rangeStart.HasValue && _rangeEnd.HasValue)
            {
                if (sender.Position >= _rangeEnd.Value || sender.Position < _rangeStart.Value)
                {
                    sender.Position = _rangeStart.Value;
                }
            }
        }
    }
}
