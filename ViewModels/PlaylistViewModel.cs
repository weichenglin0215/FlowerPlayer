using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using FlowerPlayer.Services;
using FlowerPlayer.Models;

namespace FlowerPlayer.ViewModels
{
    public partial class PlaylistViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<MediaItem> _playlistItems = new();

        private ObservableCollection<HistoryItem> _historyItems;
        public ObservableCollection<HistoryItem> HistoryItems
        {
            get => _historyItems;
            set => SetProperty(ref _historyItems, value);
        }

        [ObservableProperty]
        private bool _showWaveform;

        private readonly IMediaService _mediaService;

        public string HistoryTitle => $"History ({HistoryItems?.Count ?? 0})";

        public PlaylistViewModel(IMediaService mediaService)
        {
            _mediaService = mediaService;
            HistoryItems = new ObservableCollection<HistoryItem>();
            
            OnPropertyChanged(nameof(HistoryTitle));
        }

        [RelayCommand]
        public async Task AddFile(StorageFile file)
        {
            if (!PlaylistItems.Any(i => i.FilePath == file.Path))
            {
                var item = new MediaItem(file);
                PlaylistItems.Add(item);
                await item.LoadPropertiesAsync();
            }
        }

        [RelayCommand]
        public void RemoveFile(MediaItem item)
        {
            if (PlaylistItems.Contains(item))
            {
                PlaylistItems.Remove(item);
            }
        }

        public async Task AddToHistoryAsync(StorageFile file)
        {
            // Remove existing item with same path if exists
            var existing = HistoryItems.FirstOrDefault(h => h.FilePath == file.Path);
            if (existing != null)
            {
                HistoryItems.Remove(existing);
            }

            // Create new history item and load properties
            var historyItem = new HistoryItem(file);
            await historyItem.LoadPropertiesAsync();
            
            // Add to top
            HistoryItems.Insert(0, historyItem);
            System.Diagnostics.Debug.WriteLine($"Added history item: {file.Name}. New count: {HistoryItems.Count}");
            OnPropertyChanged(nameof(HistoryTitle));
        }

        [RelayCommand]
        public async Task OpenHistoryFile(HistoryItem item)
        {
            if (item?.File != null)
            {
                _mediaService.Open(item.File);
            }
        }
    }
}
