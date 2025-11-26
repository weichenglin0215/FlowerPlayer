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
        private ObservableCollection<StorageFile> _playlistItems = new();

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

            // Add dummy data for design time verification
            HistoryItems.Add(new HistoryItem("Dummy Video 1.mp4", "150 MB", "2023/10/01 10:00:00", "C:\\Videos\\Dummy1.mp4"));
            HistoryItems.Add(new HistoryItem("Dummy Music 2.mp3", "5 MB", "2023/10/02 11:30:00", "C:\\Music\\Dummy2.mp3"));
            HistoryItems.Add(new HistoryItem("Dummy Movie 3.mkv", "2.5 GB", "2023/10/03 20:15:00", "D:\\Movies\\Dummy3.mkv"));
            
            OnPropertyChanged(nameof(HistoryTitle));
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
