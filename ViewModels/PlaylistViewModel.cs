using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Windows.Storage;
using FlowerPlayer.Services;

namespace FlowerPlayer.ViewModels
{
    public partial class PlaylistViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<StorageFile> _playlistItems = new();

        [ObservableProperty]
        private ObservableCollection<StorageFile> _historyItems = new();

        [ObservableProperty]
        private bool _showWaveform;

        private readonly IMediaService _mediaService;

        public PlaylistViewModel(IMediaService mediaService)
        {
            _mediaService = mediaService;
        }

        [RelayCommand]
        public void AddFile(StorageFile file)
        {
            if (!PlaylistItems.Contains(file))
            {
                PlaylistItems.Add(file);
            }
        }

        [RelayCommand]
        public void RemoveFile(StorageFile file)
        {
            if (PlaylistItems.Contains(file))
            {
                PlaylistItems.Remove(file);
            }
        }

        [RelayCommand]
        public void AddToHistory(StorageFile file)
        {
            // Add to top, remove duplicates if needed
            if (HistoryItems.Contains(file))
            {
                HistoryItems.Remove(file);
            }
            HistoryItems.Insert(0, file);
        }
    }
}
