using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Media.Playback;

namespace FlowerPlayer.Services
{
    public interface IMediaService
    {
        event EventHandler<TimeSpan> PositionChanged;
        event EventHandler<MediaState> StateChanged;
        event EventHandler<StorageFile> MediaOpened;
        event EventHandler<TimeSpan> DurationChanged;
        event EventHandler MediaEnded;

        TimeSpan Duration { get; }
        TimeSpan Position { get; set; }
        MediaState CurrentState { get; }
        double Volume { get; set; }
        bool IsMuted { get; set; }
        bool IsLooping { get; set; }
        bool HasVideo { get; }
        MediaPlayer Player { get; }
        StorageFile CurrentFile { get; }

        void Open(StorageFile file);
        void Play();
        void Pause();
        void Stop();
        void StepForward(int frames = 1);
        void StepBackward(int frames = 1);
        Task<double> GetFrameRateAsync();
        void SetRange(TimeSpan start, TimeSpan end);
        void ClearRange();
        Task SaveRangeAsAsync(StorageFile destination);
    }

    public enum MediaState
    {
        Stopped,
        Playing,
        Paused
    }
}
