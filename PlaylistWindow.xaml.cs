using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using FlowerPlayer.Models;
using FlowerPlayer.Services;
using FlowerPlayer.ViewModels;
using FlowerPlayer.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.ApplicationModel.DataTransfer;

namespace FlowerPlayer
{
    public sealed partial class PlaylistWindow : Window
    {
        public PlaylistViewModel ViewModel { get; }
        // Action to report status back to MainWindow
        public Action<string> UpdateStatus { get; set; }
        private readonly IMediaService _mediaService;
        
        // 提供公共屬性訪問 PlaylistListView（用於 MainWindow 刪除檔案時）
        public ListView PlaylistListViewControl => PlaylistListView;

        public PlaylistWindow(IMediaService mediaService, ViewModels.PlaylistViewModel existingViewModel = null)
        {
            _mediaService = mediaService;
            ViewModel = existingViewModel ?? new PlaylistViewModel(mediaService);
            
            this.InitializeComponent();
            
            // 恢复窗口位置和尺寸
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                // 恢复尺寸
                var savedSize = Services.LocalSettingsService.GetWindowSize(Services.LocalSettingsService.KeyPlaylistWindowSize);
                if (savedSize.HasValue)
                {
                    appWindow.Resize(savedSize.Value);
                }
                else
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 600));
                }
                
                // 恢复位置
                var savedPosition = Services.LocalSettingsService.GetWindowPosition(Services.LocalSettingsService.KeyPlaylistWindowPosition);
                if (savedPosition.HasValue)
                {
                    appWindow.Move(savedPosition.Value);
                }
                
                // 监听位置和尺寸变化
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPositionChange || args.DidSizeChange)
                    {
                        SaveWindowState();
                    }
                };
            }
            catch { }
            
            // 注册窗口关闭事件，保存播放清单和窗口状态
            this.Closed += PlaylistWindow_Closed;
            
            // 在窗口激活后加载保存的播放清单（确保ListView已初始化）
            this.Activated += PlaylistWindow_Activated;
        }
        
        // 保存播放清单（改為公共方法，供 MainWindow 調用）
        public void SavePlaylist()
        {
            try
            {
                var paths = new List<string>();
                foreach (var item in PlaylistListView.Items)
                {
                    if (item is Grid row && row.Tag is string path)
                    {
                        paths.Add(path);
                    }
                }
                Services.LocalSettingsService.PlaylistPaths = paths;
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow: Saved {paths.Count} items to playlist");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow.SavePlaylist error: {ex.Message}");
            }
        }
        
        // 加载播放清单
        private async System.Threading.Tasks.Task LoadPlaylistAsync()
        {
            try
            {
                _isLoadingPlaylist = true; // 标记正在加载
                
                var savedPaths = Services.LocalSettingsService.PlaylistPaths;
                if (savedPaths == null || savedPaths.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("PlaylistWindow: No saved playlist found");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow: Loading {savedPaths.Count} items from saved playlist");
                int loadedCount = 0;
                
                foreach (var path in savedPaths)
                {
                    try
                    {
                        // 检查文件是否存在
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        if (file != null)
                        {
                            // 检查是否已存在（避免重复添加）
                            bool exists = false;
                            foreach (var item in PlaylistListView.Items)
                            {
                                if (item is Grid row && row.Tag is string existingPath && existingPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                                {
                                    exists = true;
                                    break;
                                }
                            }
                            
                            if (!exists)
                            {
                                // 加载时不保存，避免重复保存
                                AddFile(file, saveAfterAdd: false);
                                loadedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 文件不存在或无法访问，跳过
                        System.Diagnostics.Debug.WriteLine($"PlaylistWindow: Failed to load file {path}: {ex.Message}");
                    }
                }
                
                if (loadedCount > 0)
                {
                    UpdateStatus?.Invoke($"Playlist: 已載入 {loadedCount} 個檔案");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow.LoadPlaylistAsync error: {ex.Message}");
            }
            finally
            {
                _isLoadingPlaylist = false; // 标记加载完成
            }
        }
        
        // 窗口激活时加载播放清单（只加载一次）
        private bool _playlistLoaded = false;
        private async void PlaylistWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_playlistLoaded)
            {
                _playlistLoaded = true;
                await LoadPlaylistAsync();
            }
        }
        
        // 保存窗口状态（位置和尺寸）
        private void SaveWindowState()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                Services.LocalSettingsService.SaveWindowPosition(
                    Services.LocalSettingsService.KeyPlaylistWindowPosition, 
                    appWindow.Position);
                Services.LocalSettingsService.SaveWindowSize(
                    Services.LocalSettingsService.KeyPlaylistWindowSize, 
                    appWindow.Size);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow.SaveWindowState error: {ex.Message}");
            }
        }
        
        // 窗口关闭时保存播放清单和窗口状态
        private void PlaylistWindow_Closed(object sender, WindowEventArgs args)
        {
            SavePlaylist();
            SaveWindowState();
        }

        // ---------------------------------------------------------------------
        // Drag‑over handler for the whole window (referenced in XAML as Playlist_DragOver)
        // ---------------------------------------------------------------------
        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            // 移除状态消息，避免拖拽时频繁更新状态
            // UpdateStatus?.Invoke("Playlist: DragOver");
        }

        // ---------------------------------------------------------------------
        // Drop handler – add each dropped file to the playlist (referenced as Playlist_Drop)
        // ---------------------------------------------------------------------
        private async void Playlist_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                int addedCount = 0;
                int skippedCount = 0;
                
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        // 檢查是否為媒體檔案
                        if (MediaFileHelper.IsMediaFile(file))
                        {
                            AddFile(file);
                            addedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                }
                
                if (addedCount > 0)
                {
                    UpdateStatus?.Invoke($"Playlist: 已添加 {addedCount} 個媒體檔案");
                }
                if (skippedCount > 0)
                {
                    UpdateStatus?.Invoke($"Playlist: 已跳過 {skippedCount} 個非媒體檔案。\n{MediaFileHelper.GetSupportedFormatsDescription()}");
                }
            }
        }

        // ---------------------------------------------------------------------
        // Double‑tap a row to play the file in the main window (referenced as PlaylistListView_DoubleTapped)
        // ---------------------------------------------------------------------
        public Action<StorageFile> OpenFileAction { get; set; }

        // ---------------------------------------------------------------------
        // Double‑tap a row to play the file in the main window (referenced as PlaylistListView_DoubleTapped)
        // ---------------------------------------------------------------------
        private async void PlaylistListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            // Find the Grid row that was double‑tapped
            FrameworkElement element = e.OriginalSource as FrameworkElement;
            while (element != null && !(element is Grid && element.Tag is string))
            {
                element = element.Parent as FrameworkElement;
            }

            if (element is Grid row && row.Tag is string path)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    
                    if (OpenFileAction != null)
                    {
                        OpenFileAction(file);
                    }
                    else
                    {
                        _mediaService.Open(file);
                        _mediaService.Play();
                    }
                    
                    // 更新主窗口狀態列
                    UpdateStatus?.Invoke($"Playlist: 正在播放 {file.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlaylistWindow - Play error: {ex.Message}");
                    UpdateStatus?.Invoke($"Playlist: 播放錯誤 - {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------------
        // Right‑click context menu – delete selected items (referenced as PlaylistListView_RightTapped)
        // ---------------------------------------------------------------------
        private void PlaylistListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // 先找到被点击的项目
            FrameworkElement element = e.OriginalSource as FrameworkElement;
            while (element != null && !(element is Grid && element.Tag is string))
            {
                element = element.Parent as FrameworkElement;
            }

            // 如果找到了项目，先选中它
            if (element is Grid row && row.Tag is string path)
            {
                // 清除当前选择
                PlaylistListView.SelectedItems.Clear();
                // 选中被点击的项目
                PlaylistListView.SelectedItem = row;
            }
            
            var flyout = new MenuFlyout();
            
            // 刪除選單項
            var deleteItem = new MenuFlyoutItem { Text = "刪除" };
            deleteItem.Click += (s, args) => DeleteSelectedItems();
            flyout.Items.Add(deleteItem);
            
            // 刪除實體檔案選單項（紅色）
            var deleteFileItem = new MenuFlyoutItem { Text = "刪除實體檔案..." };
            deleteFileItem.Click += async (s, args) => await DeleteSelectedFiles();
            // 設置紅色背景
            deleteFileItem.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            flyout.Items.Add(deleteFileItem);
            
            // 分隔線
            flyout.Items.Add(new MenuFlyoutSeparator());
            
            // 刪除所有播放清單項目選單項
            var deleteAllItem = new MenuFlyoutItem { Text = "刪除所有播放清單項目" };
            deleteAllItem.Click += async (s, args) => await DeleteAllItems();
            flyout.Items.Add(deleteAllItem);
            
            // 顯示在滑鼠游標右方
            flyout.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        }

        private void DeleteSelectedItems()
        {
            var selected = new List<object>(PlaylistListView.SelectedItems);
            int count = selected.Count;
            foreach (var item in selected)
            {
                PlaylistListView.Items.Remove(item);
            }
            
            // 更新主窗口狀態列
            if (count > 0)
            {
                UpdateStatus?.Invoke($"Playlist: 已刪除 {count} 個項目");
                // 删除项目后保存播放清单
                SavePlaylist();
            }
        }
        
        // 刪除所有播放清單項目
        private async System.Threading.Tasks.Task DeleteAllItems()
        {
            int totalCount = PlaylistListView.Items.Count;
            if (totalCount == 0)
            {
                UpdateStatus?.Invoke("Playlist: 播放清單已經是空的");
                return;
            }
            
            // 顯示確認對話框
            var dialog = new ContentDialog
            {
                Title = "確認刪除所有項目",
                Content = $"確定要刪除播放清單中的所有 {totalCount} 個項目嗎？",
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            
            // 清空播放清單
            PlaylistListView.Items.Clear();
            
            // 更新主窗口狀態列
            UpdateStatus?.Invoke($"Playlist: 已刪除所有 {totalCount} 個項目");
            
            // 保存播放清單
            SavePlaylist();
        }

        private async System.Threading.Tasks.Task DeleteSelectedFiles()
        {
            // B狀況：播放清單右鍵"刪除實體檔案..."的流程
            
            // 設置刪除標誌，防止在刪除過程中觸發自動播放
            _isDeletingFiles = true;
            
            // 記錄當前是否正在播放，以便稍後恢復
            bool wasPlaying = _mediaService.CurrentState == MediaState.Playing;
            
            try
            {
                var selected = new List<object>(PlaylistListView.SelectedItems);
                if (selected.Count == 0)
                {
                    _isDeletingFiles = false;
                    return;
                }

                // 1. 無論是否選中當前播放檔案，都先暫停播放，避免干擾
                if (wasPlaying)
                {
                    _mediaService.Pause();
                }

                // 記錄當前播放的文件路徑
                string currentPath = _mediaService.CurrentFile?.Path;
                bool isCurrentFileSelected = false;
                
                if (!string.IsNullOrEmpty(currentPath))
                {
                    // 檢查當前播放的文件是否在選中列表中
                    foreach (var item in selected)
                    {
                        if (item is Grid row && row.Tag is string path && 
                            path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            isCurrentFileSelected = true;
                            break;
                        }
                    }
                }

                // 建立檔案列表字串用於對話框
                var fileNames = new System.Text.StringBuilder();
                int displayCount = 0;
                foreach (var item in selected)
                {
                    if (item is Grid row && row.Children.Count > 0 && row.Children[0] is TextBlock tb)
                    {
                        if (displayCount < 10)
                        {
                            fileNames.AppendLine($"- {tb.Text}");
                        }
                        displayCount++;
                    }
                }
                if (displayCount > 10) fileNames.AppendLine($"... 以及其他 {displayCount - 10} 個檔案");

                // 2. 顯示確認對話框
                var dialog = new ContentDialog
                {
                    Title = "確認刪除實體檔案",
                    Content = $"確定要將以下 {selected.Count} 個檔案移至資源回收桶嗎？\n\n{fileNames}",
                    PrimaryButtonText = "確定",
                    SecondaryButtonText = "取消",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                
                // 2. 若使用者取消刪除，恢復播放狀態
                if (result != ContentDialogResult.Primary)
                {
                    // 恢復播放狀態（如果之前正在播放）
                    if (wasPlaying)
                    {
                        _mediaService.Play();
                    }
                    _isDeletingFiles = false;
                    return;
                }

                // 3. 若使用者同意刪除
                // 3a. 如果當前播放的文件在選中列表中，Close() 放棄控制權
                if (isCurrentFileSelected)
                {
                    _mediaService.Close();
                }

                // 3b. 在刪除前查找下一個檔案路徑（如果當前文件會被刪除）
                string nextPathToPlay = null;
                if (isCurrentFileSelected && Services.LocalSettingsService.AutoPlayNext)
                {
                    System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Current file will be deleted, finding next file...");
                    // 建立要刪除的路徑集合，用於快速查找
                    var pathsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in selected)
                    {
                        if (item is Grid row && row.Tag is string path)
                        {
                            pathsToDelete.Add(path);
                        }
                    }
                    
                    // 暫時禁用 _isDeletingFiles 檢查，以便查找下一個文件
                    bool savedIsDeletingFiles = _isDeletingFiles;
                    _isDeletingFiles = false;
                    
                    try
                    {
                        // 查找下一個不在刪除列表中的檔案
                        string tempCurrent = currentPath;
                        int maxTries = Math.Min(PlaylistListView.Items.Count, 100);
                        int tryCount = 0;
                        
                        for (int i = 0; i < maxTries && tryCount < 100; i++)
                        {
                            string next = GetNextFilePath(tempCurrent);
                            if (string.IsNullOrEmpty(next) || next == tempCurrent)
                            {
                                nextPathToPlay = null;
                                break;
                            }
                            
                            // 檢查下一個檔案是否在刪除列表中
                            if (!pathsToDelete.Contains(next))
                            {
                                nextPathToPlay = next;
                                break;
                            }
                            
                            tempCurrent = next;
                            tryCount++;
                        }
                        
                        if (tryCount >= 100)
                        {
                            System.Diagnostics.Debug.WriteLine("PlaylistWindow - Warning: Max tries reached when finding next file");
                            nextPathToPlay = null;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Found next path to play: {nextPathToPlay}");
                    }
                    finally
                    {
                        // 恢復 _isDeletingFiles 標誌
                        _isDeletingFiles = savedIsDeletingFiles;
                    }
                }

                // 3c. 逐個嘗試刪除選中的檔案
                var deletedPaths = new List<string>();
                var failedPaths = new List<string>();
                var itemsToRemove = new List<object>();

                foreach (var item in selected)
                {
                    if (item is Grid row && row.Tag is string path)
                    {
                        try
                        {
                            var file = await StorageFile.GetFileFromPathAsync(path);
                            await file.DeleteAsync(StorageDeleteOption.Default);
                            deletedPaths.Add(path);
                            itemsToRemove.Add(item);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"PlaylistWindow - Delete file error: {ex.Message}");
                            failedPaths.Add(path);
                        }
                    }
                }

                // 3d. 若任何檔案刪除失敗，跳出警告，停止後續動作，不修改播放清單
                if (failedPaths.Count > 0)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "刪除失敗",
                        Content = $"無法刪除 {failedPaths.Count} 個檔案。\n\n請檢查檔案權限或是否被其他程式使用。",
                        PrimaryButtonText = "確定",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    
                    // 恢復播放狀態（如果之前正在播放且檔案未被刪除）
                    // 如果當前檔案被刪除，則不恢復
                    if (wasPlaying && !deletedPaths.Contains(currentPath))
                    {
                        try
                        {
                            _mediaService.Play();
                        }
                        catch { }
                    }
                    
                    _isDeletingFiles = false;
                    return;
                }

                // 4. 順利刪除實體檔案之後，從"播放清單"中比對是否有相同檔名與目錄（可能有多個），若有相同，從播放清單中移除該項目
                if (deletedPaths.Count > 0)
                {
                    // 移除所有成功刪除的檔案對應的項目
                    foreach (var item in itemsToRemove)
                    {
                        PlaylistListView.Items.Remove(item);
                    }
                    
                    // 保存播放清單
                    SavePlaylist();
                    
                    // 更新主窗口狀態列
                    UpdateStatus?.Invoke($"Playlist: 已刪除 {deletedPaths.Count} 個實體檔案");
                }

                // 5. 若"依播放清單順序，播放下一個檔案"有勾選，請載入下一個媒體檔案
                if (isCurrentFileSelected && Services.LocalSettingsService.AutoPlayNext && 
                    !string.IsNullOrEmpty(nextPathToPlay) && OpenFileAction != null)
                {
                    System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Attempting to play next file: {nextPathToPlay}");
                    
                    try
                    {
                        // 在 UI 執行緒直接等待，不切換到背景執行緒
                        await Task.Delay(500);

                        // 再次檢查檔案是否存在（可能在刪除過程中被其他操作刪除）
                        var nextFile = await StorageFile.GetFileFromPathAsync(nextPathToPlay);
                        
                        System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Executing OpenFileAction for: {nextFile.Name}");
                        OpenFileAction(nextFile);
                        System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Successfully executed OpenFileAction");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PlaylistWindow - Failed to play next after delete: {ex.Message}");
                    }
                }
                else if (wasPlaying && !isCurrentFileSelected)
                {
                    // 如果刪除的不是當前播放檔案，且原本在播放，則恢復播放
                    try
                    {
                        _mediaService.Play();
                    }
                    catch { }
                    
                    System.Diagnostics.Debug.WriteLine($"DeleteSelectedFiles: Skipping auto-play, resuming current. wasPlaying={wasPlaying}, isCurrentFileSelected={isCurrentFileSelected}");
                }
            }
            finally
            {
                // 清除刪除標誌
                _isDeletingFiles = false;
            }
        }


        private bool _isLoadingPlaylist = false; // 标记是否正在加载播放清单
        private bool _isDeletingFiles = false; // 标记是否正在删除文件，防止在删除过程中触发自动播放
        
        public void AddFile(Windows.Storage.StorageFile file, bool saveAfterAdd = true)
        {
            try
            {
                var props = file.GetBasicPropertiesAsync().AsTask().Result;
                var directory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                var duration = "Unknown"; // Placeholder, could be calculated later
                var modified = props.DateModified.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
                var fileSize = FormatFileSize(props.Size);

                // Create a Grid representing a row with five columns
                var rowGrid = new Grid();
                // Column definitions must match the header widths
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(600) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(120) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(120) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(200) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });

                // File name
                var tbName = new TextBlock
                {
                    Text = file.Name,
                    Margin = new Thickness(5, 2, 5, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(tbName, 0);
                rowGrid.Children.Add(tbName);

                // File size
                var tbSize = new TextBlock
                {
                    Text = fileSize,
                    Margin = new Thickness(5, 2, 5, 2)
                };
                Grid.SetColumn(tbSize, 1);
                rowGrid.Children.Add(tbSize);

                // Duration
                var tbDur = new TextBlock
                {
                    Text = duration,
                    Margin = new Thickness(5, 2, 5, 2)
                };
                Grid.SetColumn(tbDur, 2);
                rowGrid.Children.Add(tbDur);

                // Modified date
                var tbMod = new TextBlock
                {
                    Text = modified,
                    Margin = new Thickness(5, 2, 5, 2)
                };
                Grid.SetColumn(tbMod, 3);
                rowGrid.Children.Add(tbMod);

                // Directory
                var tbDir = new TextBlock
                {
                    Text = directory,
                    Margin = new Thickness(5, 2, 5, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(tbDir, 4);
                rowGrid.Children.Add(tbDir);

                // 將文件路徑存儲在 Tag 中，以便雙擊時可以獲取
                rowGrid.Tag = file.Path;

                // Add the row to the ListView
                PlaylistListView.Items.Add(rowGrid);
                System.Diagnostics.Debug.WriteLine($"Added to playlist: {file.Name}");
                // 移除状态消息，避免每次添加文件时都显示消息
                // UpdateStatus?.Invoke($"Playlist: Added {file.Name}");
                
                // 只有在非加载状态下才保存播放清单
                if (saveAfterAdd && !_isLoadingPlaylist)
                {
                    SavePlaylist();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding file to playlist: {ex.Message}");
            }
        }
        
        // 格式化文件大小（类似Windows文件总管的显示方式，通常以KB为单位）
        private string FormatFileSize(ulong bytes)
        {
            // Windows文件总管的显示规则：
            // - 小于1KB：显示为字节（B）
            // - 1KB到1MB：显示为KB，保留2位小数
            // - 1MB到1GB：显示为MB，保留2位小数
            // - 1GB以上：显示为GB，保留2位小数
            
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            else if (bytes < 1024 * 1024)
            {
                // KB
                double kb = bytes / 1024.0;
                return $"{kb:F2} KB";
            }
            else if (bytes < 1024UL * 1024 * 1024)
            {
                // MB
                double mb = bytes / (1024.0 * 1024.0);
                return $"{mb:F2} MB";
            }
            else
            {
                // GB
                double gb = bytes / (1024.0 * 1024.0 * 1024.0);
                return $"{gb:F2} GB";
            }
        }

        // 獲取下一個文件路徑（用於自動播放）
        public string? GetNextFilePath(string? currentPath)
        {
            // 如果正在删除文件，返回null，避免在删除过程中查找下一个文件
            if (_isDeletingFiles)
            {
                System.Diagnostics.Debug.WriteLine("GetNextFilePath: Skipping because files are being deleted");
                return null;
            }
            
            bool foundCurrent = false;
            System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Looking for next file after {currentPath}, Total items: {PlaylistListView.Items.Count}");
            
            // 创建项目列表的快照，避免在遍历过程中列表被修改
            var itemsSnapshot = new List<object>();
            try
            {
                foreach (var item in PlaylistListView.Items)
                {
                    itemsSnapshot.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Error creating snapshot: {ex.Message}");
                return null;
            }
            
            foreach (var item in itemsSnapshot)
            {
                if (item is Grid row && row.Tag is string path)
                {
                    System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Checking item: {path}");
                    if (foundCurrent)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Found next file: {path}");
                        return path;
                    }
                    if (string.IsNullOrEmpty(currentPath) || path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Found current file: {path}");
                        foundCurrent = true;
                        if (string.IsNullOrEmpty(currentPath))
                        {
                            return path;
                        }
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine("GetNextFilePath: No next file found");
            return null;
        }

        // 獲取上一個文件路徑
        public string? GetPreviousFilePath(string? currentPath)
        {
            string? previousPath = null;
            System.Diagnostics.Debug.WriteLine($"GetPreviousFilePath: Looking for previous file before {currentPath}, Total items: {PlaylistListView.Items.Count}");
            
            foreach (var item in PlaylistListView.Items)
            {
                if (item is Grid row && row.Tag is string path)
                {
                    if (string.IsNullOrEmpty(currentPath) || path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"GetPreviousFilePath: Found current file: {path}, Previous: {previousPath}");
                        return previousPath;
                    }
                    previousPath = path;
                }
            }
            System.Diagnostics.Debug.WriteLine("GetPreviousFilePath: No previous file found");
            return null;
        }

        // 根據路徑移除文件
        public void RemoveFileByPath(string filePath)
        {
            var itemsToRemove = new List<object>();
            foreach (var item in PlaylistListView.Items)
            {
                if (item is Grid row && row.Tag is string path && path.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    itemsToRemove.Add(item);
                }
            }
            
            foreach (var item in itemsToRemove)
            {
                PlaylistListView.Items.Remove(item);
            }
            
            if (itemsToRemove.Count > 0)
            {
                UpdateStatus?.Invoke($"Playlist: 已從清單中移除檔案");
            }
        }

        // 根據路徑選擇文件（用於顯示當前播放的文件）
        public void SelectFileByPath(string filePath)
        {
            PlaylistListView.SelectedItems.Clear();
            
            foreach (var item in PlaylistListView.Items)
            {
                if (item is Grid row && row.Tag is string path && path.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    PlaylistListView.SelectedItem = item;
                    // 滾動到選中的項目
                    PlaylistListView.ScrollIntoView(item);
                    break;
                }
            }
        }
    }
}
