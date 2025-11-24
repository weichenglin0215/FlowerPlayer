using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using FlowerPlayer.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Dispatching;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FlowerPlayer
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        public Converters.TimeConverter TimeFormatConverter { get; } = new Converters.TimeConverter();
        private DispatcherTimer _positionTimer;
        private PlaylistWindow _playlistWindow = null;

        public MainWindow()
        {
            this.InitializeComponent();
            
            ViewModel = new MainViewModel();
            RootGrid.DataContext = ViewModel;
            
            // 註冊鍵盤事件處理到 RootGrid
            RootGrid.KeyDown += MainWindow_KeyDown;

            // Set up timer for smooth position updates
            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(100); // Update 10 times per second
            _positionTimer.Tick += PositionTimer_Tick;
            
            // Connect MediaPlayer
            PlayerElement.SetMediaPlayer(ViewModel.MediaService.Player);
            
            // Connect events with DispatcherQueue for thread-safe UI updates
            var dispatcherQueue = DispatcherQueue;
            
            ViewModel.MediaService.PositionChanged += (s, position) =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.CurrentTime = position;
                    ViewModel.SliderPosition = position.TotalSeconds;
                });
            };
            
            ViewModel.MediaService.StateChanged += (s, state) =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.IsPlaying = state == Services.MediaState.Playing;

                    // Start/stop position timer based on playback state
                    if (state == Services.MediaState.Playing)
                    {
                        _positionTimer.Start();
                    }
                    else
                    {
                        _positionTimer.Stop();
                    }
                });
            };
            
            ViewModel.MediaService.MediaOpened += async (s, file) =>
            {
                // Get frame rate
                var fps = await ViewModel.MediaService.GetFrameRateAsync();
                
                dispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.WindowTitle = $"FlowerPlayer - {file.Name}";
                    ViewModel.HasVideo = ViewModel.MediaService.HasVideo;
                    ViewModel.IsFileLoaded = true;
                    ViewModel.GenerateWaveform(file);
                    // Update converter frame rate
                    TimeFormatConverter.FrameRate = fps;
                });
            };

            ViewModel.MediaService.DurationChanged += (s, duration) =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.TotalDuration = duration;
                    // Force update slider maximum
                    TimelineSlider.Maximum = duration.TotalSeconds;

                    // Set RangeEnd to the end of the video
                    ViewModel.RangeEnd = duration.TotalSeconds;
                    // Ensure the visual representation updates correctly
                    TimelineSlider.RangeEnd = duration.TotalSeconds;
                    
                    ViewModel.RangeStart = 0;
                    ViewModel.SliderPosition = 0;
                    ViewModel.CurrentTime = TimeSpan.Zero;
                });
            };
            
            // Connect slider value changing event to seek media
            TimelineSlider.ValueChanging += (s, newValue) =>
            {
                ViewModel.MediaService.Position = TimeSpan.FromSeconds(newValue);
            };
            
            // Connect range sliders to seek media to start/end points
            TimelineSlider.RangeStartChanging += (s, newValue) =>
            {
                ViewModel.MediaService.Position = TimeSpan.FromSeconds(newValue);
            };

            TimelineSlider.RangeEndChanging += (s, newValue) =>
            {
                ViewModel.MediaService.Position = TimeSpan.FromSeconds(newValue);
            };
            
            this.Title = "FlowerPlayer";
            
            // 設置焦點到 RootGrid 以便接收鍵盤輸入
            this.Activated += (s, e) => RootGrid.Focus(FocusState.Programmatic);
        }

        public string FormatTime(TimeSpan time)
        {
            return (string)TimeFormatConverter.Convert(time, typeof(string), null, null);
        }

        private async void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");

            // Initialize the picker with the window handle
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ViewModel.OpenFile(file);
            }
        }

        private void OpenPlaylist_Click(object sender, RoutedEventArgs e)
        {
            // 確保只有一個播放清單視窗
            if (_playlistWindow == null)
            {
                _playlistWindow = new PlaylistWindow(ViewModel.MediaService);
                _playlistWindow.Closed += (s, args) => _playlistWindow = null;
                _playlistWindow.Activate();
            }
            else
            {
                // 如果視窗已經存在，則將焦點設置到該視窗
                _playlistWindow.Activate();
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            // Update position during playback for smooth slider movement
            var position = ViewModel.MediaService.Position;
            ViewModel.CurrentTime = position;
            ViewModel.SliderPosition = position.TotalSeconds;
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
                {
                    ViewModel.OpenFile(file);
                }
            }
        }

        private void MediaArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 只有在檔案已載入且是滑鼠左鍵點擊時才觸發
            if (ViewModel.IsFileLoaded && e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                ViewModel.PlayPauseCommand?.Execute(null);
                
                // 如果是雙擊，可以考慮全螢幕等其他功能，目前僅單擊播放/暫停
                // 為了避免與拖曳衝突，這裡不設置 e.Handled = true，除非確定只是點擊
                // 但對於簡單的播放暫停，通常可以接受
            }
        }

        private void MainWindow_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 檢查修飾鍵
            var isShiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // 如果沒有載入檔案，只允許開啟檔案和播放清單的操作
            if (!ViewModel.IsFileLoaded)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.O:
                        if (isCtrlPressed)
                        {
                            OpenFile_Click(this, null);
                            e.Handled = true;
                        }
                        break;
                    case Windows.System.VirtualKey.L:
                        if (isCtrlPressed)
                        {
                            OpenPlaylist_Click(this, null);
                            e.Handled = true;
                        }
                        break;
                }
                return;
            }

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Right:
                    if (isShiftPressed)
                    {
                        // Shift+右方向鍵：+1分鐘（音樂和影片都可用）
                        ViewModel.SeekForwardCommand?.Execute(null);
                        e.Handled = true;
                    }
                    else if (ViewModel.HasVideo)
                    {
                        // 右方向鍵：下一幀（僅影片檔案可用）
                        ViewModel.StepNextFrameCommand?.Execute(null);
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.Left:
                    if (isShiftPressed)
                    {
                        // Shift+左方向鍵：-1分鐘（音樂和影片都可用）
                        ViewModel.SeekBackwardCommand?.Execute(null);
                        e.Handled = true;
                    }
                    else if (ViewModel.HasVideo)
                    {
                        // 左方向鍵：上一幀（僅影片檔案可用）
                        ViewModel.StepPrevFrameCommand?.Execute(null);
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.Space:
                    // Space：播放/暫停
                    ViewModel.PlayPauseCommand?.Execute(null);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.O:
                    if (isCtrlPressed)
                    {
                        // Ctrl+O：開啟檔案
                        OpenFile_Click(this, null);
                        e.Handled = true;
                    }
                    break;

                case Windows.System.VirtualKey.L:
                    if (isCtrlPressed)
                    {
                        // Ctrl+L：開啟播放清單視窗
                        OpenPlaylist_Click(this, null);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Services.LocalSettingsService.ResumeLastFile)
            {
                var lastPath = Services.LocalSettingsService.LastFilePath;
                if (!string.IsNullOrEmpty(lastPath))
                {
                    try
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(lastPath);
                        if (file != null)
                        {
                            ViewModel.OpenFile(file);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore errors (e.g. file not found, no permission)
                    }
                }
            }
        }
    }
}
