using FlowerPlayer.Helpers;
using FlowerPlayer.Models;
using FlowerPlayer.Services;
using FlowerPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FlowerPlayer
{
    public sealed partial class PlaylistWindow : Window
    {
        public PlaylistViewModel ViewModel { get; }
        // Action to report status back to MainWindow
        public Action<string> UpdateStatus { get; set; }
        private readonly IMediaService _mediaService;
        // 這才是 ListView 的真正資料來源
        //public ObservableCollection<PlaylistDisplayItem> PlaylistDisplayItem { get; } = new();
        // 提供公共屬性訪問 PlaylistListView（用於 MainWindow 刪除檔案時）
        public ListView PlaylistListViewControl => PlaylistListView;

        public PlaylistWindow(IMediaService mediaService, ViewModels.PlaylistViewModel existingViewModel = null)
        {
            _mediaService = mediaService;
            ViewModel = existingViewModel ?? new PlaylistViewModel(mediaService);
            
            this.InitializeComponent();
            // 重要！把 ObservableCollection 綁定到 ListView
            //PlaylistListView.ItemsSource = PlaylistDisplayItem;
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
            
            // 监听列宽变化，同步到 ListView 项目（在 InitializeComponent 之后才能访问 HeaderGrid）
            this.HeaderGrid.SizeChanged += HeaderGrid_SizeChanged;
        }
        
        // 拖拽调整列宽的变量
        private bool _isResizing = false;
        private Border _currentSplitter = null;
        private int _leftColumnIndex = -1;
        private double _startX = 0;
        private double _leftColumnStartWidth = 0;
        
        // 注意：WinUI3 中设置光标比较复杂，这里暂时移除光标设置功能
        // 拖拽功能本身仍然可以正常工作
        private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // WinUI3 中设置光标需要使用其他方法，暂时不实现
        }
        
        private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // WinUI3 中设置光标需要使用其他方法，暂时不实现
        }
        
        // Splitter 拖拽处理
        private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border splitter)
            {
                _isResizing = true;
                _currentSplitter = splitter;
                _startX = e.GetCurrentPoint(this.HeaderGrid).Position.X;
                
                // 确定要调整的列索引
                int splitterColumn = Grid.GetColumn(splitter);
                _leftColumnIndex = splitterColumn - 1; // Splitter 左边的列
                
                if (_leftColumnIndex >= 0 && _leftColumnIndex < this.HeaderGrid.ColumnDefinitions.Count)
                {
                    var leftCol = this.HeaderGrid.ColumnDefinitions[_leftColumnIndex];
                    _leftColumnStartWidth = leftCol.ActualWidth;
                }
                
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }
        
        private void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing && _currentSplitter != null && _leftColumnIndex >= 0)
            {
                double currentX = e.GetCurrentPoint(this.HeaderGrid).Position.X;
                double deltaX = currentX - _startX;
                
                if (_leftColumnIndex < this.HeaderGrid.ColumnDefinitions.Count)
                {
                    var leftCol = this.HeaderGrid.ColumnDefinitions[_leftColumnIndex];
                    double newWidth = _leftColumnStartWidth + deltaX;
                    
                    // 限制最小宽度
                    if (newWidth < 50) newWidth = 50;
                    
                    leftCol.Width = new GridLength(newWidth);
                    
                    // 同步到 ListView 项目
                    SyncColumnWidths();
                }
            }
        }
        
        private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing && _currentSplitter != null)
            {
                _currentSplitter.ReleasePointerCapture(e.Pointer);
                _isResizing = false;
                _currentSplitter = null;
                _leftColumnIndex = -1;
                e.Handled = true;
            }
        }
        
        // 同步列宽到所有 ListView 项目
        private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncColumnWidths();
        }
        
        // 当 ListView 项目加载时同步列宽
        private void PlaylistItemGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Grid itemGrid)
            {
                SyncItemGridColumns(itemGrid);
            }
        }
        
        // 同步单个项目的列宽
        private void SyncItemGridColumns(Grid itemGrid)
        {
            if (itemGrid.ColumnDefinitions.Count >= 5 && this.HeaderGrid.ColumnDefinitions.Count >= 9)
            {
                // 跳过 GridSplitter 列（索引 1, 3, 5, 7）
                itemGrid.ColumnDefinitions[0].Width = this.HeaderGrid.ColumnDefinitions[0].Width;
                itemGrid.ColumnDefinitions[1].Width = this.HeaderGrid.ColumnDefinitions[2].Width;
                itemGrid.ColumnDefinitions[2].Width = this.HeaderGrid.ColumnDefinitions[4].Width;
                itemGrid.ColumnDefinitions[3].Width = this.HeaderGrid.ColumnDefinitions[6].Width;
                itemGrid.ColumnDefinitions[4].Width = this.HeaderGrid.ColumnDefinitions[8].Width;
            }
        }
        
        // 同步所有 ListView 项目的列宽
        private void SyncColumnWidths()
        {
            foreach (var item in PlaylistListView.Items)
            {
                var container = PlaylistListView.ContainerFromItem(item);
                if (container is ListViewItem listViewItem)
                {
                    var contentPresenter = listViewItem.ContentTemplateRoot as Grid;
                    if (contentPresenter != null)
                    {
                        SyncItemGridColumns(contentPresenter);
                    }
                }
            }
        }
        
        // 保存播放清单（改為公共方法，供 MainWindow 調用）
        public void SavePlaylist()
        {
            try
            {
                var paths = new List<string>();
                foreach (var item in PlaylistListView.Items)
                {
                    if (item is PlaylistDisplayItem displayItem)
                    {
                        paths.Add(displayItem.FullPath);
                    }
                    else if (item is Grid row && row.Tag is string path)
                    {
                        // 兼容旧格式
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
                                string existingPath = null;
                                if (item is PlaylistDisplayItem displayItem)
                                {
                                    existingPath = displayItem.FullPath;
                                }
                                else if (item is Grid row && row.Tag is string tagPath)
                                {
                                    existingPath = tagPath;
                                }
                                
                                if (existingPath != null && existingPath.Equals(path, StringComparison.OrdinalIgnoreCase))
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
        private async void PlaylistWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
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
                
                _isLoadingPlaylist = true; // 避免頻繁觸發 SavePlaylist
                try
                {
                    foreach (var item in items)
                    {
                        if (item is StorageFile file)
                        {
                            // 檢查是否為媒體檔案
                            if (MediaFileHelper.IsMediaFile(file))
                            {
                                AddFile(file, saveAfterAdd: false);
                                addedCount++;
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        else if (item is StorageFolder folder)
                        {
                            addedCount += await AddFolderRecursiveAsync(folder);
                        }
                    }
                }
                finally
                {
                    _isLoadingPlaylist = false;
                    if (addedCount > 0)
                    {
                        SavePlaylist();
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

        private async Task<int> AddFolderRecursiveAsync(StorageFolder folder)
        {
            int count = 0;
            try
            {
                // 1. 處理當前目錄的檔案
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    if (MediaFileHelper.IsMediaFile(file))
                    {
                        AddFile(file, saveAfterAdd: false);
                        count++;
                    }
                }

                // 2. 遞迴處理子目錄
                var subFolders = await folder.GetFoldersAsync();
                foreach (var subFolder in subFolders)
                {
                    count += await AddFolderRecursiveAsync(subFolder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning folder {folder.Name}: {ex.Message}");
            }
            return count;
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
            string path = null;
            
            // 尝试从选中的项目获取路径
            if (PlaylistListView.SelectedItem is PlaylistDisplayItem displayItem)
            {
                path = displayItem.FullPath;
            }
            else if (PlaylistListView.SelectedItem is Grid row && row.Tag is string tagPath)
            {
                path = tagPath;
            }
            else
            {
                // 如果没有选中项，尝试从点击的元素查找
                FrameworkElement element = e.OriginalSource as FrameworkElement;
                while (element != null)
                {
                    if (element is ListViewItem listViewItem)
                    {
                        if (listViewItem.Content is PlaylistDisplayItem item)
                        {
                            path = item.FullPath;
                            break;
                        }
                        else if (listViewItem.Content is Grid grid && grid.Tag is string gridPath)
                        {
                            path = gridPath;
                            break;
                        }
                    }
                    element = element.Parent as FrameworkElement;
                }
            }

            if (!string.IsNullOrEmpty(path))
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
        // Selection changed handler – output selected item content (referenced as PlaylistListView_SelectionChanged)
        // ---------------------------------------------------------------------
        private void PlaylistListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistListView.SelectedItem is PlaylistDisplayItem selectedItem)
            {
                // 输出选中项目的所有字段内容
                System.Diagnostics.Debug.WriteLine("========================================");
                System.Diagnostics.Debug.WriteLine($"播放清單項目已選中 (從 ListView 讀取):");
                System.Diagnostics.Debug.WriteLine($"  檔案名稱: [{"選取" + selectedItem.FileName ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine($"  容量大小: [{selectedItem.FileSize ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine($"  長度: [{selectedItem.Duration ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine($"  修改日期: [{selectedItem.ModifiedDate ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine($"  目錄: [{selectedItem.Directory ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine($"  完整路徑: [{selectedItem.FullPath ?? "(null)"}]");
                System.Diagnostics.Debug.WriteLine("========================================");
            }
            else if (PlaylistListView.SelectedItem != null)
            {
                System.Diagnostics.Debug.WriteLine($"警告: 選中的項目不是 PlaylistDisplayItem 類型，而是: {PlaylistListView.SelectedItem.GetType().Name}");
            }
        }

        // ---------------------------------------------------------------------
        // Right‑click context menu – delete selected items (referenced as PlaylistListView_RightTapped)
        // ---------------------------------------------------------------------
        private void PlaylistListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // 先找到被点击的项目
            object clickedItem = null;
            FrameworkElement element = e.OriginalSource as FrameworkElement;
            System.Diagnostics.Debug.WriteLine($"點選起點: [{element?.GetType().Name}]");

            while (element != null)
            {
                System.Diagnostics.Debug.WriteLine($"檢查元素: [{element.GetType().Name}]");
                if (element is ListViewItem listViewItem)
                {
                    clickedItem = listViewItem.Content;
                    System.Diagnostics.Debug.WriteLine($"找到 ListViewItem，內容: [{clickedItem}]");
                    break;
                }
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
                System.Diagnostics.Debug.WriteLine($"點選的項目: [{element}]");
            }

            // 如果找到了项目
            if (clickedItem != null)
            {
                System.Diagnostics.Debug.WriteLine($"準備設置選擇項目");

                // 檢查點擊的項目是否已經在選取範圍內
                bool isAlreadySelected = PlaylistListView.SelectedItems.Contains(clickedItem);

                if (isAlreadySelected)
                {
                    // 如果點擊的項目已經被選取，保留現有的多選狀態
                    System.Diagnostics.Debug.WriteLine($"點擊的項目已在選取範圍內，保留多選狀態");
                }
                else
                {
                    // 如果點擊的項目沒有被選取，清除舊選擇並選取新項目
                    System.Diagnostics.Debug.WriteLine($"點擊的項目不在選取範圍內，更新選擇");
                    PlaylistListView.SelectedItems.Clear();
                    PlaylistListView.SelectedItem = clickedItem;
                    System.Diagnostics.Debug.WriteLine($"置換選擇項目: [{PlaylistListView.SelectedItem ?? "(null)"}]");
                }
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
                        string itemPath = null;
                        if (item is PlaylistDisplayItem displayItem)
                        {
                            itemPath = displayItem.FullPath;
                        }
                        else if (item is Grid row && row.Tag is string tagPath)
                        {
                            itemPath = tagPath;
                        }
                        
                        if (itemPath != null && itemPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
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
                    string fileName = null;
                    if (item is PlaylistDisplayItem displayItem)
                    {
                        fileName = displayItem.FileName;
                    }
                    else if (item is Grid row && row.Children.Count > 0 && row.Children[0] is TextBlock tb)
                    {
                        fileName = tb.Text;
                    }
                    
                    if (fileName != null)
                    {
                        if (displayCount < 10)
                        {
                            fileNames.AppendLine($"- {fileName}");
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
                        string path = null;
                        if (item is PlaylistDisplayItem displayItem)
                        {
                            path = displayItem.FullPath;
                        }
                        else if (item is Grid row && row.Tag is string tagPath)
                        {
                            path = tagPath;
                        }
                        
                        if (path != null)
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
                    string path = null;
                    if (item is PlaylistDisplayItem displayItem)
                    {
                        path = displayItem.FullPath;
                    }
                    else if (item is Grid row && row.Tag is string tagPath)
                    {
                        path = tagPath;
                    }
                    
                    if (path != null)
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

        // 這段是魔法！不管你用 Items.Add 還是什麼方式加進去，都會自動填文字
        private void PlaylistListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;           // 回收的項目跳過
            if (args.Item is not PlaylistDisplayItem item) return;

            // 重要：Phase 0 才能拿到真正的 ContentTemplateRoot
            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(PlaylistListView_ContainerContentChanging); // 進入下一階段
            }
            else if (args.Phase == 1)
            {
                // 找到我們在 XAML 命名的 TextBlock
                var grid = args.ItemContainer.ContentTemplateRoot as Grid;
                if (grid != null)
                {
                    var tb1 = grid.FindName("TbFileName") as TextBlock;
                    var tb2 = grid.FindName("TbFileSize") as TextBlock;
                    var tb3 = grid.FindName("TbDuration") as TextBlock;
                    var tb4 = grid.FindName("TbModifiedDate") as TextBlock;
                    var tb5 = grid.FindName("TbDirectory") as TextBlock;

                    if (tb1 != null) tb1.Text = item.FileName ?? "";
                    if (tb2 != null) tb2.Text = item.FileSize ?? "";
                    if (tb3 != null) tb3.Text = item.Duration ?? "";
                    if (tb4 != null) tb4.Text = item.ModifiedDate ?? "";
                    if (tb5 != null) tb5.Text = item.Directory ?? "";
                }

                args.Handled = true; // 告訴系統我們已經處理完畢
            }
        }
        private async void BtnOpenFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                // WinUI 3 需要設置 Window Handle
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                
                // 添加支援的檔案類型
                foreach (var ext in MediaFileHelper.AudioExtensions)
                {
                    picker.FileTypeFilter.Add(ext);
                }
                foreach (var ext in MediaFileHelper.VideoExtensions)
                {
                    picker.FileTypeFilter.Add(ext);
                }
                // 如果沒有定義擴展名，至少添加一個通配符或常見格式
                if (picker.FileTypeFilter.Count == 0)
                {
                    picker.FileTypeFilter.Add("*");
                }

                var files = await picker.PickMultipleFilesAsync();
                if (files.Count > 0)
                {
                    _isLoadingPlaylist = true;
                    int addedCount = 0;
                    foreach (var file in files)
                    {
                        AddFile(file, saveAfterAdd: false);
                        addedCount++;
                    }
                    _isLoadingPlaylist = false;
                    SavePlaylist();
                    
                    UpdateStatus?.Invoke($"Playlist: 已添加 {addedCount} 個檔案");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnOpenFiles_Click error: {ex.Message}");
            }
        }

        private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add("*"); // FolderPicker 需要這個

                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    _isLoadingPlaylist = true;
                    int addedCount = await AddFolderRecursiveAsync(folder);
                    _isLoadingPlaylist = false;
                    
                    if (addedCount > 0)
                    {
                        SavePlaylist();
                        UpdateStatus?.Invoke($"Playlist: 已從目錄添加 {addedCount} 個檔案");
                    }
                    else
                    {
                        UpdateStatus?.Invoke($"Playlist: 該目錄沒有發現媒體檔案");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BtnOpenFolder_Click error: {ex.Message}");
            }
        }

        private async void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            await DeleteAllItems();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 暴力遍歷 ListView 內所有已經生成的項目
            for (int i = 0; i < PlaylistListView.Items.Count; i++)
            {
                var item = PlaylistListView.Items[i] as PlaylistDisplayItem;
                if (item == null) continue;

                // 取得這個項目對應的 ListViewItem 容器
                var container = PlaylistListView.ContainerFromIndex(i) as ListViewItem;
                if (container == null) continue; // 還沒滾到、還沒生成就跳過

                // 強制這個容器載入模板（這行很重要！）
                container.UpdateLayout();

                // 現在一定拿得到 Grid 和 TextBlock
                if (container.ContentTemplateRoot is Grid grid)
                {
                    var tb1 = grid.FindName("TbFileName") as TextBlock;
                    var tb2 = grid.FindName("TbFileSize") as TextBlock;
                    var tb3 = grid.FindName("TbDuration") as TextBlock;
                    var tb4 = grid.FindName("TbModifiedDate") as TextBlock;
                    var tb5 = grid.FindName("TbDirectory") as TextBlock;

                    // 安全寫法，就算有 null 也不會炸
                    if (tb1 != null) tb1.Text = item.FileName ?? "";
                    if (tb2 != null) tb2.Text = item.FileSize ?? "";
                    if (tb3 != null) tb3.Text = item.Duration ?? "";
                    if (tb4 != null) tb4.Text = item.ModifiedDate ?? "";
                    if (tb5 != null) tb5.Text = item.Directory ?? "";
                }
            }
        }
        public void AddFile(Windows.Storage.StorageFile file, bool saveAfterAdd = true)
        {
            try
            {
                var props = file.GetBasicPropertiesAsync().AsTask().Result;
                var directory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                var duration = "Unknown"; // Placeholder, could be calculated later
                var modified = props.DateModified.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
                var fileSize = FormatFileSize(props.Size);

                // Create a PlaylistDisplayItem for data binding
                var displayItem = new PlaylistDisplayItem
                {
                    FileName = file.Name,
                    FileSize = fileSize,
                    //Duration = "不知道",
                    Duration = duration,
                    ModifiedDate = modified,
                    Directory = directory,
                    FullPath = file.Path
                };

                // Add the item to the ListView (will use ItemTemplate)
                PlaylistListView.Items.Add(displayItem);

               // 从 ListView 中读取刚添加的项目，确认数据已正确存储
               var addedItem = PlaylistListView.Items[PlaylistListView.Items.Count - 1] as PlaylistDisplayItem;
                if (addedItem != null)
                {
                    // 2. 強制 ListView 立刻產生所有 UI 元素（關閉虛擬化效果）
                    PlaylistListView.UpdateLayout();                   // 強制排版
                    PlaylistListView.ScrollIntoView(addedItem);             // 強制產生該項目的容器

                    // 3. 現在就能拿到剛剛加入的那一筆的 Grid 了！
                    if (PlaylistListView.ContainerFromItem(addedItem) is ListViewItem listViewItem)
                    {
                        // 這行是真正的救命仙丹！！！
                        var container = PlaylistListView.ContainerFromItem(addedItem) as ListViewItem;
                        container?.UpdateLayout();   // 強制這個 ListViewItem 自己 Apply Template
                        if (listViewItem.ContentTemplateRoot is Grid grid)
                        {
                            var tb1 = grid.FindName("TbFileName") as TextBlock;
                            var tb2 = grid.FindName("TbFileSize") as TextBlock;
                            var tb3 = grid.FindName("TbDuration") as TextBlock;
                            var tb4 = grid.FindName("TbModifiedDate") as TextBlock;
                            var tb5 = grid.FindName("TbDirectory") as TextBlock;

                            if (tb1 != null) tb1.Text = addedItem.FileName ?? "";
                            if (tb2 != null) tb2.Text = addedItem.FileSize ?? "";
                            if (tb3 != null) tb3.Text = addedItem.Duration ?? "";
                            if (tb4 != null) tb4.Text = addedItem.ModifiedDate ?? "";
                            if (tb5 != null) tb5.Text = addedItem.Directory ?? "";


                            //tb1.Text = addedItem.FileName;
                            //System.Diagnostics.Debug.WriteLine($"  檔案名稱: [{tb1.Text}]");
                            //tb2.Text = addedItem.FileSize;
                            //tb3.Text = addedItem.Duration;
                            //tb4.Text = addedItem.ModifiedDate;
                            //tb5.Text = addedItem.Directory;
                        }
                    }

                    // 输出从播放清单项目中读取的所有文字内容到调试输出
                    System.Diagnostics.Debug.WriteLine("========================================");
                    System.Diagnostics.Debug.WriteLine($"播放清單項目已添加 (從 ListView 讀取):");
                    System.Diagnostics.Debug.WriteLine($"  檔案名稱: [{addedItem.FileName}]");
                    System.Diagnostics.Debug.WriteLine($"  容量大小: [{addedItem.FileSize}]");
                    System.Diagnostics.Debug.WriteLine($"  長度: [{addedItem.Duration}]");
                    System.Diagnostics.Debug.WriteLine($"  修改日期: [{addedItem.ModifiedDate}]");
                    System.Diagnostics.Debug.WriteLine($"  目錄: [{addedItem.Directory}]");
                    System.Diagnostics.Debug.WriteLine($"  完整路徑: [{addedItem.FullPath}]");
                    System.Diagnostics.Debug.WriteLine("========================================");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"警告: 無法從 ListView 讀取剛添加的項目 (類型: {PlaylistListView.Items[PlaylistListView.Items.Count - 1]?.GetType().Name})");
                }
                
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
                string itemPath = null;
                if (item is PlaylistDisplayItem displayItem)
                {
                    itemPath = displayItem.FullPath;
                }
                else if (item is Grid row && row.Tag is string tagPath)
                {
                    itemPath = tagPath;
                }
                
                if (itemPath != null)
                {
                    System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Checking item: {itemPath}");
                    if (foundCurrent)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Found next file: {itemPath}");
                        return itemPath;
                    }
                    if (string.IsNullOrEmpty(currentPath) || itemPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Found current file: {itemPath}");
                        foundCurrent = true;
                        if (string.IsNullOrEmpty(currentPath))
                        {
                            return itemPath;
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
                string path = null;
                if (item is PlaylistDisplayItem displayItem)
                {
                    path = displayItem.FullPath;
                }
                else if (item is Grid row && row.Tag is string tagPath)
                {
                    path = tagPath;
                }
                
                if (path != null)
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
                string path = null;
                if (item is PlaylistDisplayItem displayItem)
                {
                    path = displayItem.FullPath;
                }
                else if (item is Grid row && row.Tag is string tagPath)
                {
                    path = tagPath;
                }
                
                if (path != null && path.Equals(filePath, StringComparison.OrdinalIgnoreCase))
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
                string path = null;
                if (item is PlaylistDisplayItem displayItem)
                {
                    path = displayItem.FullPath;
                }
                else if (item is Grid row && row.Tag is string tagPath)
                {
                    path = tagPath;
                }
                
                if (path != null && path.Equals(filePath, StringComparison.OrdinalIgnoreCase))
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
