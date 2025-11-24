using Microsoft.UI.Xaml;
using FlowerPlayer.ViewModels;
using Windows.Storage.Pickers;
using System;
using WinRT.Interop;

using FlowerPlayer.Services;

namespace FlowerPlayer
{
    public sealed partial class PlaylistWindow : Window
    {
        public PlaylistViewModel ViewModel { get; }
        private readonly IMediaService _mediaService;

        public PlaylistWindow(IMediaService mediaService)
        {
            this.InitializeComponent();
            _mediaService = mediaService;
            ViewModel = new PlaylistViewModel(mediaService);
        }

        private async void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ViewModel.AddFile(file);
            }
        }

        private async void SaveRange_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeChoices.Add("Video", new System.Collections.Generic.List<string>() { ".mp4" });
            picker.SuggestedFileName = "TrimmedVideo";
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await _mediaService.SaveRangeAsAsync(file);
            }
        }

        private SettingsWindow _settingsWindow;

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (s, args) => _settingsWindow = null;
                _settingsWindow.Activate();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFile file)
                    {
                        ViewModel.AddFile(file);
                    }
                }
            }
        }
    }
}
