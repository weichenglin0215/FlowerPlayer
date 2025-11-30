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
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Dispatching;
using FlowerPlayer.Helpers;

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
        private HistoryWindow _historyWindow = null;
        private SettingsWindow _settingsWindow = null;

        public MainWindow()
        {
            this.InitializeComponent();
            
            ViewModel = new MainViewModel();
            ViewModel.XamlRoot = RootGrid.XamlRoot;
            RootGrid.DataContext = ViewModel;
            
            // 恢復主視窗位置和尺寸
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                // 恢復尺寸
                var savedSize = Services.LocalSettingsService.GetWindowSize(Services.LocalSettingsService.KeyMainWindowSize);
                if (savedSize.HasValue)
                {
                    appWindow.Resize(savedSize.Value);
                }
                else
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(1600, 800));
                }
                
                // 恢復位置
                var savedPosition = Services.LocalSettingsService.GetWindowPosition(Services.LocalSettingsService.KeyMainWindowPosition);
                if (savedPosition.HasValue)
                {
                    appWindow.Move(savedPosition.Value);
                }
                
                // 監聽位置和尺寸變化
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPositionChange || args.DidSizeChange)
                    {
                        SaveMainWindowState();
                    }
                };
            }
            catch { }
            
            // 註冊窗口關閉事件，關閉時一併關閉其他視窗並保存窗口狀態
            this.Closed += MainWindow_Closed;
            
            // 註冊鍵盤事件處理到 RootGrid
            RootGrid.KeyDown += MainWindow_KeyDown;

            // Set up timer for smooth position updates
            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(100); // Update 10 times per second
            _positionTimer.Tick += PositionTimer_Tick;
            
            // Connect MediaPlayer
            PlayerElement.SetMediaPlayer(ViewModel.MediaService.Player);
            
            // 設置 ViewModel 的 XamlRoot（用於顯示對話框）
            ViewModel.XamlRoot = RootGrid.XamlRoot;
            
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

                    // 更新播放按鈕文字（確保顯示正確）
                    if (PlayPauseButton != null)
                    {
                        PlayPauseButton.Content = ViewModel.IsPlaying ? "暫停" : "播放";
                    }

                    // 更新狀態列第一欄（播放狀態）
                    if (state == Services.MediaState.Playing)
                    {
                        ViewModel.StatusMessage = GetStatusMessageWithSkipInfo("正在播放");
                        _positionTimer.Start();
                    }
                    else if (state == Services.MediaState.Paused)
                    {
                        if (ViewModel.MediaService.IsStopped)
                        {
                            ViewModel.StatusMessage = "已停止播放";
                        }
                        else
                        {
                            ViewModel.StatusMessage = GetStatusMessageWithSkipInfo("已暫停");
                        }
                        _positionTimer.Stop();
                    }
                    else
                    {
                        // 只有在檔案已載入時才顯示"已停止"，避免覆蓋"無法正常開啟..."等錯誤訊息
                        if (ViewModel.MediaService.CurrentFile != null && ViewModel.StatusMessage != "無法正常開啟媒體檔案")
                        {
                            ViewModel.StatusMessage = "已停止";
                        }
                        _positionTimer.Stop();
                    }

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
                if (file == null)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.IsFileLoaded = false;
                        ViewModel.WindowTitle = "FlowerPlayer";
                        ViewModel.CurrentFileName = string.Empty;
                        ViewModel.CurrentFileDirectory = string.Empty;
                        ViewModel.TotalDuration = TimeSpan.Zero;
                        ViewModel.CurrentTime = TimeSpan.Zero;
                        ViewModel.SliderPosition = 0;
                        ViewModel.WaveformData = null;
                        ViewModel.HasVideo = false;
                        // 清空媒體時隱藏播放器
                        PlayerElement.Opacity = 0;
                        // ViewModel.StatusMessage = "已清除"; // Optional
                    });
                    return;
                }

                // Get frame rate
                var fps = await ViewModel.MediaService.GetFrameRateAsync();
                
                dispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.WindowTitle = $"FlowerPlayer - {file.Name}";
                    ViewModel.HasVideo = ViewModel.MediaService.HasVideo;
                    ViewModel.IsFileLoaded = true;
                    
                    // 重置顯示波形按鈕（不清空緩存的波形數據）
                    ViewModel.IsWaveformVisible = false;
                    
                    // Update converter frame rate
                    TimeFormatConverter.FrameRate = fps;
                    
                    // 更新狀態列
                    ViewModel.StatusMessage = "已開啟";
                    ViewModel.CurrentFileName = file.Name;
                    ViewModel.CurrentFileDirectory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                    
                    // 新媒體載入完成，顯示播放器
                    PlayerElement.Opacity = 1;
                    
                    // Add to history
                    Services.LocalSettingsService.AddHistoryPath(file.Path);
                    
                    // Refresh history window if it's open
                    if (_historyWindow != null)
                    {
                        _historyWindow.Refresh();
                    }
                    
                    // 在播放清單中選擇當前文件
                    if (_playlistWindow != null)
                    {
                        _playlistWindow.SelectFileByPath(file.Path);
                    }
                });
            };

            ViewModel.MediaService.DurationChanged += (s, duration) =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] DurationChanged fired. New Duration: {duration.TotalSeconds}s");

                    // 記錄當前狀態
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Before update - Current Duration: {ViewModel.TotalDuration.TotalSeconds}s, RangeStart: {ViewModel.RangeStart}s, RangeEnd: {ViewModel.RangeEnd}s, SliderPosition: {ViewModel.SliderPosition}s");

                    // 如果 duration 为 0 或无效，说明媒体已关闭，不更新 RangeSlider
                    if (duration.TotalSeconds <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Duration is zero/invalid. Resetting properties.");
                        // 媒体已关闭，重置相关属性但不触发 RangeSlider 更新
                        ViewModel.SliderPosition = 0;
                        ViewModel.CurrentTime = TimeSpan.Zero;
                        // 不设置 RangeStart 和 RangeEnd，避免触发 RangeSlider 更新
                        return;
                    }

                    // 檢查是否是極短的媒體檔案（可能有問題），如果是就記錄但不重置位置
                    if (duration.TotalSeconds < 1.0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Warning: Very short media duration ({duration.TotalSeconds}s). Skipping position reset.");
                        // 仍然更新 Duration，但不重置位置
                        ViewModel.TotalDuration = duration;
                        ViewModel.RangeEnd = duration.TotalSeconds;
                        return;
                    }

                    // 1. 先重置播放位置到 0，避免因為縮小 Maximum 而導致 Value 超出範圍觸發 Coercion
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Resetting SliderPosition and RangeStart to 0.");
                    ViewModel.SliderPosition = 0;
                    ViewModel.CurrentTime = TimeSpan.Zero;
                    ViewModel.RangeStart = 0;

                    // 2. 更新 TotalDuration (這會更新 TimelineSlider.Maximum)
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Updating ViewModel.TotalDuration to {duration.TotalSeconds}");
                    ViewModel.TotalDuration = duration;

                    // 3. 更新 RangeEnd
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Updating ViewModel.RangeEnd to {duration.TotalSeconds}");
                    ViewModel.RangeEnd = duration.TotalSeconds;

                    // 4. 應用跳過前面秒數的設置
                    double skipSeconds = Services.LocalSettingsService.SkipStartSeconds;
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Checking SkipStartSeconds: {skipSeconds}s");

                    if (skipSeconds > 0 && skipSeconds < duration.TotalSeconds)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Applying SkipStartSeconds: {skipSeconds}s");
                        ViewModel.MediaService.Position = TimeSpan.FromSeconds(skipSeconds);
                        ViewModel.RangeStart = skipSeconds;
                        ViewModel.SliderPosition = skipSeconds;
                        ViewModel.CurrentTime = TimeSpan.FromSeconds(skipSeconds);

                        // Sync Smart Skip start to the new position
                        if (ViewModel.IsSmartSkipActive)
                        {
                            ViewModel.SyncSmartSkipStart();
                        }
                    }

                    // 5. 現在媒體已完全載入，如果需要自動播放，在此開始播放
                    if (Services.LocalSettingsService.AutoPlayOnOpen)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] AutoPlayOnOpen is enabled, starting playback after media loaded.");
                        ViewModel.MediaService.Play();
                    }

                    // 記錄最終狀態
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] After update - Final Duration: {ViewModel.TotalDuration.TotalSeconds}s, RangeStart: {ViewModel.RangeStart}s, RangeEnd: {ViewModel.RangeEnd}s, SliderPosition: {ViewModel.SliderPosition}s");
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] DurationChanged processing completed.");
                });
            };

            ViewModel.MediaService.MediaEnded += (s, e) =>
            {
                var position = ViewModel.MediaService.Position.TotalSeconds;
                var duration = ViewModel.TotalDuration.TotalSeconds;
                var rangeStart = ViewModel.RangeStart;
                var rangeEnd = ViewModel.RangeEnd;
                
                System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaEnded event triggered.");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] State: Position={position}s, Duration={duration}s");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Slider: RangeStart={rangeStart}s, RangeEnd={rangeEnd}s");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Settings: AutoPlayNext={Services.LocalSettingsService.AutoPlayNext}, IsLooping={ViewModel.IsLooping}");
                
                // 防止誤觸發：如果播放時間太短（例如小於0.5秒）且總長度大於1秒，可能是開啟瞬間的誤報
                if (position < 0.5 && duration > 1.0)
                {
                     System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaEnded ignored (premature trigger). Position near 0.");
                     return;
                }

                // 嚴格檢查：只有當 Position 接近 Duration 或 RangeEnd 時，才視為真正結束
                bool isAtEnd = (Math.Abs(position - duration) <= 1.0) || (Math.Abs(position - rangeEnd) <= 1.0);

                if (!isAtEnd)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaEnded ignored: Position not at end. (Diff Duration: {Math.Abs(position - duration)}, Diff RangeEnd: {Math.Abs(position - rangeEnd)})");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaEnded accepted: Playback truly ended.");

                // 如果啟用了自動播放下一個檔案，且未啟用循環播放，則播放下一個檔案
                if (Services.LocalSettingsService.AutoPlayNext && !ViewModel.IsLooping && _playlistWindow != null)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Calling PlayNextFileInPlaylist from MediaEnded");
                        _ = PlayNextFileInPlaylist();
                    });
                }
            };

            ViewModel.MediaService.MediaFailed += (s, args) =>
            {
                 dispatcherQueue.TryEnqueue(() =>
                 {
                     System.Diagnostics.Debug.WriteLine($"[DEBUG] MediaFailed: {args.Error} - {args.ErrorMessage}");
                     ViewModel.StatusMessage = "無法正常開啟媒體檔案";
                     // Optionally close/reset player state if needed, but keeping the message is key.
                     // We might want to stop the timer.
                     _positionTimer.Stop();
                     ViewModel.IsPlaying = false;
                     if (PlayPauseButton != null) PlayPauseButton.Content = "播放";
                 });
            };
            
            // Connect slider value changing event to seek media
            TimelineSlider.ValueChanging += (s, newValue) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TimelineSlider_ValueChanging: {newValue}");
                ViewModel.MediaService.Position = TimeSpan.FromSeconds(newValue);
                if (ViewModel.IsSmartSkipActive) ViewModel.SyncSmartSkipStart();
            };
            
            // Connect range sliders to seek media to start/end points
            // 注意：拖动范围滑块时不应该改变播放位置，只应该更新范围
            // 移除 Position 设置，避免循环调用
            TimelineSlider.RangeStartChanging += (s, newValue) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TimelineSlider_RangeStartChanging: {newValue}");
                // 拖动 RangeStart 时，只更新 ViewModel.RangeStart（TwoWay 绑定会自动处理）
                // 不设置 Position，避免循环调用
            };

            TimelineSlider.RangeEndChanging += (s, newValue) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TimelineSlider_RangeEndChanging: {newValue}");
                // 拖动 RangeEnd 时，只更新 ViewModel.RangeEnd（TwoWay 绑定会自动处理）
                // 不设置 Position，避免循环调用
            };
            
            //this.Title = "FlowerPlayer";
            
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
            
            // 添加所有支援的媒體檔案格式
            foreach (var ext in MediaFileHelper.AllMediaExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            // Initialize the picker with the window handle
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                    // 檢查是否為媒體檔案
                    if (MediaFileHelper.IsMediaFile(file))
                    {
                        // 開始載入新媒體前，先隱藏播放器，避免顯示舊媒體畫面
                        PlayerElement.Opacity = 0;
                        
                        ViewModel.StatusMessage = "正在開啟";
                        ViewModel.OpenFile(file);
                    }
                    else
                    {
                        ViewModel.StatusMessage = $"錯誤: {file.Name} 不是支援的媒體檔案格式";
                        ViewModel.CurrentFileName = file.Name;
                        ViewModel.CurrentFileDirectory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                    }
            }
        }

        private void OpenPlaylist_Click(object sender, RoutedEventArgs e)
        {
            // 確保只有一個播放清單視窗
            if (_playlistWindow == null)
            {
                _playlistWindow = new PlaylistWindow(ViewModel.MediaService);
                _playlistWindow.UpdateStatus = msg => ViewModel.StatusMessage = msg;
                _playlistWindow.OpenFileAction = ViewModel.OpenFile;
                _playlistWindow.Closed += (s, args) => _playlistWindow = null;
                _playlistWindow.Activate();
            }
            else
            {
                // 如果視窗已經存在，則將焦點設置到該視窗
                _playlistWindow.Activate();
            }
        }

        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            // 確保只有一個歷史清單視窗
            if (_historyWindow == null)
            {
                _historyWindow = new HistoryWindow(ViewModel.MediaService);
                _historyWindow.UpdateStatus = msg => ViewModel.StatusMessage = msg;
                _historyWindow.OpenFileAction = ViewModel.OpenFile;
                _historyWindow.Closed += (s, args) => _historyWindow = null;
                _historyWindow.Activate();
            }
            else
            {
                // 如果視窗已經存在，則將焦點設置到該視窗
                _historyWindow.Activate();
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            // 確保只有一個設定視窗
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (s, args) => _settingsWindow = null;
                _settingsWindow.Activate();
            }
            else
            {
                // 如果視窗已經存在，則將焦點設置到該視窗
                _settingsWindow.Activate();
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            ViewModel.StatusMessage = "拖曳檔案到此處以開啟";
            // 不清除文件名和目录，保持显示当前文件信息
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
                    // 檢查是否為媒體檔案
                    if (MediaFileHelper.IsMediaFile(file))
                    {
                        // 開始載入新媒體前，先隱藏播放器，避免顯示舊媒體畫面
                        PlayerElement.Opacity = 0;
                        
                        ViewModel.StatusMessage = "正在開啟";
                        ViewModel.OpenFile(file);
                    }
                    else
                    {
                        ViewModel.StatusMessage = $"錯誤: {file.Name} 不是支援的媒體檔案格式";
                        ViewModel.CurrentFileName = file.Name;
                        ViewModel.CurrentFileDirectory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                    }
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

        // 保存主窗口状态（位置和尺寸）
        private void SaveMainWindowState()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                Services.LocalSettingsService.SaveWindowPosition(
                    Services.LocalSettingsService.KeyMainWindowPosition, 
                    appWindow.Position);
                Services.LocalSettingsService.SaveWindowSize(
                    Services.LocalSettingsService.KeyMainWindowSize, 
                    appWindow.Size);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.SaveMainWindowState error: {ex.Message}");
            }
        }
        
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // 保存主窗口状态
            SaveMainWindowState();
            
            // 關閉所有子視窗
            _playlistWindow?.Close();
            _historyWindow?.Close();
            _settingsWindow?.Close();
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

            // SHIFT+DEL: 刪除當前檔案
            if (isShiftPressed && e.Key == Windows.System.VirtualKey.Delete)
            {
                _ = DeleteCurrentFile();
                e.Handled = true;
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

        private async void PlayPreviousFile_Click(object sender, RoutedEventArgs e)
        {
            await PlayPreviousFile();
        }

        private async void PlayNextFile_Click(object sender, RoutedEventArgs e)
        {
            await PlayNextFile();
        }

        private async System.Threading.Tasks.Task PlayPreviousFile()
        {
            try
            {
                var currentFile = ViewModel.MediaService.CurrentFile;
                if (currentFile == null) return;

                string? targetPath = null;

                // 優先從播放清單中獲取
                if (_playlistWindow != null)
                {
                    targetPath = _playlistWindow.GetPreviousFilePath(currentFile.Path);
                }

                // 如果播放清單中沒有，從目錄中獲取
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = await GetPreviousFileInDirectory(currentFile);
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(targetPath);
                    
                    // 開始載入新媒體前，先隱藏播放器，避免顯示舊媒體畫面
                    PlayerElement.Opacity = 0;
                    
                    ViewModel.StatusMessage = "正在播放上一個檔案";
                    ViewModel.OpenFile(file);
                    // AutoPlayOnOpen 設定會在 DurationChanged 事件中處理
                }
                else
                {
                    ViewModel.StatusMessage = "沒有上一個媒體檔案";
                    ViewModel.MediaService.Stop();
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"播放上一個檔案錯誤: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"MainWindow - PlayPreviousFile error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task PlayNextFile()
        {
            try
            {
                var currentFile = ViewModel.MediaService.CurrentFile;
                if (currentFile == null) return;

                string? targetPath = null;

                // 優先從播放清單中獲取
                if (_playlistWindow != null)
                {
                    targetPath = _playlistWindow.GetNextFilePath(currentFile.Path);
                }

                // 如果播放清單中沒有，從目錄中獲取
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = await GetNextFileInDirectory(currentFile);
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(targetPath);
                    
                    // 開始載入新媒體前，先隱藏播放器，避免顯示舊媒體畫面
                    PlayerElement.Opacity = 0;
                    
                    ViewModel.StatusMessage = "正在播放下一個檔案";
                    ViewModel.OpenFile(file);
                    // AutoPlayOnOpen 設定會在 DurationChanged 事件中處理
                }
                else
                {
                    ViewModel.StatusMessage = "沒有下一個媒體檔案";
                    ViewModel.MediaService.Stop();
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"播放下一個檔案錯誤: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"MainWindow - PlayNextFile error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<string?> GetPreviousFileInDirectory(StorageFile currentFile)
        {
            try
            {
                var folder = await currentFile.GetParentAsync();
                var files = await folder.GetFilesAsync();
                
                // 過濾媒體檔案並排序
                var mediaFiles = files
                    .Where(f => MediaFileHelper.IsMediaFile(f))
                    .OrderBy(f => f.Name)
                    .ToList();

                var currentIndex = mediaFiles.FindIndex(f => f.Path.Equals(currentFile.Path, StringComparison.OrdinalIgnoreCase));
                
                if (currentIndex > 0)
                {
                    return mediaFiles[currentIndex - 1].Path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetPreviousFileInDirectory error: {ex.Message}");
            }
            return null;
        }

        private async System.Threading.Tasks.Task<string?> GetNextFileInDirectory(StorageFile currentFile)
        {
            try
            {
                var folder = await currentFile.GetParentAsync();
                var files = await folder.GetFilesAsync();
                
                // 過濾媒體檔案並排序
                var mediaFiles = files
                    .Where(f => MediaFileHelper.IsMediaFile(f))
                    .OrderBy(f => f.Name)
                    .ToList();

                var currentIndex = mediaFiles.FindIndex(f => f.Path.Equals(currentFile.Path, StringComparison.OrdinalIgnoreCase));
                
                if (currentIndex >= 0 && currentIndex < mediaFiles.Count - 1)
                {
                    return mediaFiles[currentIndex + 1].Path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetNextFileInDirectory error: {ex.Message}");
            }
            return null;
        }

        private async System.Threading.Tasks.Task DeleteCurrentFile()
        {
            var currentFile = ViewModel.MediaService.CurrentFile;
            if (currentFile == null)
            {
                ViewModel.StatusMessage = "沒有正在播放的檔案";
                return;
            }

            // A狀況：主畫面按下 Shift+Del 的流程
            
            // 1. 立刻暫停播放，記下該檔案的目錄與檔名
            string currentPath = currentFile.Path;
            string currentDirectory = System.IO.Path.GetDirectoryName(currentPath) ?? string.Empty;
            string currentFileName = currentFile.Name;
            bool wasPlaying = ViewModel.IsPlaying;
            
            // 暫停播放
            ViewModel.MediaService.Pause();
            
            // 顯示確認對話框
            var dialog = new ContentDialog
            {
                Title = "確認刪除",
                Content = $"確定要將檔案「{currentFileName}」移至資源回收桶嗎？",
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                // 用戶取消，恢復播放狀態
                if (wasPlaying)
                {
                    ViewModel.MediaService.Play();
                }
                return;
            }

            try
            {
                // 2. 比對"播放清單"中是否有相同的檔案（可能多個），記錄這些項目
                var playlistItemsToRemove = new List<object>();
                if (_playlistWindow != null)
                {
                    foreach (var item in _playlistWindow.PlaylistListViewControl.Items)
                    {
                        if (item is Grid row && row.Tag is string path && 
                            path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            playlistItemsToRemove.Add(item);
                        }
                    }
                }

                // 3. 在刪除前查找下一個檔案路徑（避免刪除後找不到）
                string nextPath = null;
                if (Services.LocalSettingsService.AutoPlayNext && _playlistWindow != null)
                {
                    nextPath = _playlistWindow.GetNextFilePath(currentPath);
                    System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile: Found next path before delete: {nextPath}");
                }

                // 4. 放棄該檔案的控制權，執行刪除實體檔案的動作
                // 注意：先嘗試刪除，如果失敗則不 Close，以便恢復播放
                bool deleteSuccess = false;
                try
                {
                    // 先 Close 放棄控制權
                    ViewModel.MediaService.Close();
                    
                    // 嘗試刪除檔案
                    await currentFile.DeleteAsync(StorageDeleteOption.Default);
                    deleteSuccess = true;
                }
                catch (Exception deleteEx)
                {
                    // 刪除失敗，跳出警告，停止後續動作
                    var errorDialog = new ContentDialog
                    {
                        Title = "刪除失敗",
                        Content = $"無法刪除檔案「{currentFileName}」：\n{deleteEx.Message}\n\n請檢查檔案權限或是否被其他程式使用。",
                        PrimaryButtonText = "確定",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    
                    // 刪除失敗，不修改播放清單，直接返回
                    // 注意：由於已經 Close()，無法恢復播放，這是預期行為
                    return;
                }
                
                // 只有刪除成功才繼續後續操作
                if (!deleteSuccess) return;
                
                // 5. 確認已刪除，從播放清單中移除所有匹配的項目
                ViewModel.StatusMessage = $"已將「{currentFileName}」移至資源回收桶";
                
                if (_playlistWindow != null && playlistItemsToRemove.Count > 0)
                {
                    foreach (var item in playlistItemsToRemove)
                    {
                        _playlistWindow.PlaylistListViewControl.Items.Remove(item);
                    }
                    // 調用 SavePlaylist 方法
                    _playlistWindow.SavePlaylist();
                }

                // 6. 若順利刪除且"依播放清單順序，播放下一個檔案"有勾選，請載入下一個媒體檔案
                if (Services.LocalSettingsService.AutoPlayNext && !string.IsNullOrEmpty(nextPath))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile: Attempting to play next file: {nextPath}");
                        var nextFile = await StorageFile.GetFileFromPathAsync(nextPath);
                        ViewModel.OpenFile(nextFile);
                        System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile: Successfully opened next file: {nextFile.Name}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile: Failed to play next file after delete: {ex.Message}");
                        ViewModel.StatusMessage = $"已刪除檔案，但無法播放下一個檔案：{ex.Message}";
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile: AutoPlayNext={Services.LocalSettingsService.AutoPlayNext}, nextPath={nextPath}");
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"刪除檔案錯誤: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"DeleteCurrentFile error: {ex.Message}");
                
                // 發生錯誤時，嘗試恢復播放狀態
                if (wasPlaying && ViewModel.MediaService.CurrentFile != null)
                {
                    try
                    {
                        ViewModel.MediaService.Play();
                    }
                    catch { }
                }
            }
        }

        private async System.Threading.Tasks.Task PlayNextFileInPlaylist()
        {
            if (_playlistWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("PlayNextFileInPlaylist: PlaylistWindow is null");
                return;
            }

            try
            {
                // 獲取當前播放的文件路徑
                var currentFile = ViewModel.MediaService.CurrentFile;
                string currentPath = currentFile?.Path;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] PlayNextFileInPlaylist: Current path = {currentPath}");

                // 使用 PlaylistWindow 的公共方法獲取下一個文件路徑
                string nextPath = _playlistWindow.GetNextFilePath(currentPath);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] PlayNextFileInPlaylist: Next path = {nextPath}");

                // 如果找到下一個文件，播放它
                if (!string.IsNullOrEmpty(nextPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(nextPath);
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] PlayNextFileInPlaylist: Opening file {file.Name}");

                    // 確保當前播放完全停止
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] PlayNextFileInPlaylist: Stopping current playback first.");
                    ViewModel.MediaService.Stop();
                    
                    // 開始載入新媒體前，先隱藏播放器，避免顯示舊媒體畫面
                    PlayerElement.Opacity = 0;
                    
                    // 等待一小段時間確保停止完成
                    await System.Threading.Tasks.Task.Delay(100);

                    // 直接使用 ViewModel.OpenFile，它會處理所有清理和載入邏輯
                    // DurationChanged 事件會在媒體完全載入後自動開始播放（如果 AutoPlayOnOpen 開啟）
                    ViewModel.StatusMessage = "正在播放下一個檔案";
                    ViewModel.OpenFile(file);
                    
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] PlayNextFileInPlaylist: Successfully initiated opening next file: {file.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] PlayNextFileInPlaylist: No next file found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] MainWindow - PlayNextFileInPlaylist error: {ex.Message}");
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

        /// <summary>
        /// 生成狀態消息，如果設定了跳過秒數，則添加提示信息
        /// </summary>
        private string GetStatusMessageWithSkipInfo(string baseMessage)
        {
            double skipSeconds = Services.LocalSettingsService.SkipStartSeconds;
            if (skipSeconds > 0)
            {
                return $"{baseMessage}，設定：已跳過前面{skipSeconds:F0}秒";
            }
            return baseMessage;
        }
    }
}
