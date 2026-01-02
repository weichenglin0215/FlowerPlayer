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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Windows.Storage.Streams;
using System.Threading;
using System.Collections.Concurrent;

namespace FlowerPlayer
{
    public sealed partial class PlaylistWindow : Window
    {
        // 檔案名稱（固定）
        private const string PlaylistFileName = "PlaylistData.json";
        public PlaylistViewModel ViewModel { get; }

        private string _lastSortColumn = string.Empty;
        private bool _isAscending = true;

        private void Header_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is TextBlock header)
            {
                string column = header.Tag as string;
                if (column == _lastSortColumn)
                {
                    _isAscending = !_isAscending;
                }
                else
                {
                    _lastSortColumn = column;
                    _isAscending = true;
                }

                SortPlaylist(column, _isAscending);
                UpdateHeaderArrows();
            }
        }

        private void SortPlaylist(string column, bool ascending)
        {
            // 記錄目前選取的項目路徑，以便排序後恢復選取
            var selectedPaths = PlaylistListView.SelectedItems
                .Cast<PlaylistDisplayItem>()
                .Select(i => i.FullPath)
                .ToList();

            var items = PlaylistListView.Items.Cast<PlaylistDisplayItem>().ToList();
            
            IEnumerable<PlaylistDisplayItem> sortedItems = column switch
            {
                "FileName" => ascending ? items.OrderBy(i => i.FileName) : items.OrderByDescending(i => i.FileName),
                "FileSize" => ascending ? items.OrderBy(i => i.RawSize) : items.OrderByDescending(i => i.RawSize),
                "Duration" => ascending ? items.OrderBy(i => i.RawDuration) : items.OrderByDescending(i => i.RawDuration),
                "ModifiedDate" => ascending ? items.OrderBy(i => i.RawModifiedDate) : items.OrderByDescending(i => i.RawModifiedDate),
                "Directory" => ascending ? items.OrderBy(i => i.Directory) : items.OrderByDescending(i => i.Directory),
                _ => items
            };

            var newList = sortedItems.ToList();
            
            _isLoadingPlaylist = true; // 暫時禁用自動保存或事件觸發
            PlaylistListView.Items.Clear();
            foreach (var item in newList)
            {
                PlaylistListView.Items.Add(item);
            }
            _isLoadingPlaylist = false;

            // 恢復選取狀態
            foreach (var item in PlaylistListView.Items.Cast<PlaylistDisplayItem>())
            {
                if (selectedPaths.Contains(item.FullPath))
                {
                    PlaylistListView.SelectedItems.Add(item);
                }
            }
            
            // 排序後保存
            SavePlaylist();
        }

        private void UpdateHeaderArrows()
        {
            // 重置所有標題文字
            HdrFileName.Text = "檔案目錄"; // 依照使用者要求更名
            HdrFileSize.Text = "容量大小";
            HdrDuration.Text = "長度";
            HdrModifiedDate.Text = "修改日期";
            HdrDirectory.Text = "目錄";

            string arrow = _isAscending ? "▼" : "▲"; // 依照使用者指定：第一次(順向) ▼，第二次(反向) ▲

            switch (_lastSortColumn)
            {
                case "FileName": HdrFileName.Text += arrow; break;
                case "FileSize": HdrFileSize.Text += arrow; break;
                case "Duration": HdrDuration.Text += arrow; break;
                case "ModifiedDate": HdrModifiedDate.Text += arrow; break;
                case "Directory": HdrDirectory.Text += arrow; break;
            }
        }
        // Action to report status back to MainWindow
        public Action<string> UpdateStatus { get; set; }
        private readonly IMediaService _mediaService;
        // 這才是 ListView 的真正資料來源
        //public ObservableCollection<PlaylistDisplayItem> PlaylistDisplayItem { get; } = new();
        // 提供公共屬性訪問 PlaylistListView（用於 MainWindow 刪除檔案時）
        public ListView PlaylistListViewControl => PlaylistListView;

        // 檔案重新命名事件，通知 MainWindow 更新顯示
        public event Action<string, string> FileRenamed;

        public PlaylistWindow(IMediaService mediaService, ViewModels.PlaylistViewModel existingViewModel = null)
        {
            _mediaService = mediaService;
            ViewModel = existingViewModel ?? new PlaylistViewModel(mediaService);
            
            this.InitializeComponent();
            // 重要！把 ObservableCollection 綁定到 ListView
            //PlaylistListView.ItemsSource = PlaylistDisplayItem;
            // 恢復視窗位置和尺寸
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                // 恢復尺寸
                var savedSize = Services.LocalSettingsService.GetWindowSize(Services.LocalSettingsService.KeyPlaylistWindowSize);
                if (savedSize.HasValue)
                {
                    appWindow.Resize(savedSize.Value);
                }
                else
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 600));
                }
                
                // 恢復位置
                var savedPosition = Services.LocalSettingsService.GetWindowPosition(Services.LocalSettingsService.KeyPlaylistWindowPosition);
                if (savedPosition.HasValue)
                {
                    appWindow.Move(savedPosition.Value);
                }
                
                // 監聽位置和尺寸變化
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPositionChange || args.DidSizeChange)
                    {
                        // Enforce minimum size (100x100)
                        bool resized = false;
                        var currentSize = appWindow.Size;
                        var newWidth = currentSize.Width;
                        var newHeight = currentSize.Height;

                        if (newWidth < 100)
                        {
                            newWidth = 100;
                            resized = true;
                        }
                        if (newHeight < 100)
                        {
                            newHeight = 100;
                            resized = true;
                        }

                        if (resized)
                        {
                            appWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
                        }

                        SaveWindowState();
                    }
                };
            }
            catch { }
            
            // 註冊視窗關閉事件，儲存播放清單和視窗狀態
            this.Closed += PlaylistWindow_Closed;
            
            // 在視窗激活後載入儲存的播放清單（確保ListView已初始化）
            this.Activated += PlaylistWindow_Activated;
            
            // 監聽列寬變化，同步到 ListView 項目（在 InitializeComponent 之後才能訪問 HeaderGrid）
            this.HeaderGrid.SizeChanged += HeaderGrid_SizeChanged;
        }
        
        // 拖拽調整列寬的變數
        private bool _isResizing = false;
        private Border _currentSplitter = null;
        private int _leftColumnIndex = -1;
        private double _startX = 0;
        private double _leftColumnStartWidth = 0;
        
        // 注意：WinUI3 中設置光標比較複雜，這裡暫時移除光標設置功能
        // 拖拽功能本身仍然可以正常工作
        private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // WinUI3 中設置光標需要使用其他方法，暫時不實現
        }
        
        private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // WinUI3 中設置光標需要使用其他方法，暫時不實現
        }
        
        // Splitter 拖拽處理
        private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border splitter)
            {
                _isResizing = true;
                _currentSplitter = splitter;
                _startX = e.GetCurrentPoint(this.HeaderGrid).Position.X;
                
                // 確定要調整的列索引
                int splitterColumn = Grid.GetColumn(splitter);
                _leftColumnIndex = splitterColumn - 1; // Splitter 左邊的列
                
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
                    
                    // 限制最小寬度
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

        // 取得播放清單的完整檔案路徑
        private async Task<StorageFile> GetPlaylistFileAsync()
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            return await localFolder.CreateFileAsync(PlaylistFileName, CreationCollisionOption.OpenIfExists);
        }

        // 儲存播放清單到 LocalFolder 的 JSON 檔案
        private async Task SavePlaylistToFileAsync_OLD2()
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
                        // 兼容舊格式
                        paths.Add(path);
                    }
                }

                var file = await GetPlaylistFileAsync();
                string json = JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true });
                await FileIO.WriteTextAsync(file, json);

                System.Diagnostics.Debug.WriteLine($"PlaylistWindow: Saved {paths.Count} items to LocalFolder/{PlaylistFileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow.SavePlaylistToFileAsync error: {ex.Message}");
            }
        }

        private async Task SavePlaylistToFileAsync()
        {
            try
            {
                var items = new List<PlaylistDisplayItem>();
                foreach (var item in PlaylistListView.Items)
                {
                    if (item is PlaylistDisplayItem displayItem)
                    {
                        items.Add(displayItem);
                    }
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(items, options);

                var file = await ApplicationData.Current.LocalFolder
                    .CreateFileAsync(PlaylistFileName, CreationCollisionOption.ReplaceExisting);

                await FileIO.WriteTextAsync(file, json);

                System.Diagnostics.Debug.WriteLine($"Playlist saved: {items.Count} items → LocalFolder");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SavePlaylistToFileAsync error: {ex.Message}");
            }
        }

        // 保存播放清单（改為公共方法，供 MainWindow 調用）
        public void SavePlaylist_OLD()
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
                        // 兼容舊格式
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

        public async void SavePlaylist()
        {
            try
            {
                // 新的主要儲存方式：存到檔案
                await SavePlaylistToFileAsync();

                // （可選）你還是可以保留舊的 LocalSettingsService 做為「備份」或「快速預覽」
                // 但千萬不要再存完整清單！可以只存個數量或前幾筆給 UI 快速顯示
                // 例如：
                // var preview = paths.Take(10).ToList();
                // Services.LocalSettingsService.PlaylistPaths = preview;

                System.Diagnostics.Debug.WriteLine("PlaylistWindow: Playlist saved successfully to file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistWindow.SavePlaylist error: {ex.Message}");
            }
        }

        private async Task LoadPlaylistFromFileAsync()
        {
            try
            {
                _isLoadingPlaylist = true;

                var localFolder = ApplicationData.Current.LocalFolder;
                var fileItem = await localFolder.TryGetItemAsync(PlaylistFileName) as StorageFile;

                if (fileItem == null)
                {
                    System.Diagnostics.Debug.WriteLine("PlaylistWindow: No saved playlist file found.");
                    return;
                }

                string json = await FileIO.ReadTextAsync(fileItem);
                if (string.IsNullOrWhiteSpace(json))
                {
                    System.Diagnostics.Debug.WriteLine("PlaylistWindow: Playlist file is empty");
                    File.Delete(fileItem.Path); // 可能是損壞或舊格式，刪除之
                    return;
                }

                List<PlaylistDisplayItem> savedItems = null;
                try 
                {
                    savedItems = JsonSerializer.Deserialize<List<PlaylistDisplayItem>>(json);
                }
                catch (JsonException)
                {
                    // 嘗試兼容舊格式 (List<string>)
                    try 
                    {
                        var oldPaths = JsonSerializer.Deserialize<List<string>>(json);
                        if (oldPaths != null)
                        {
                            System.Diagnostics.Debug.WriteLine("PlaylistWindow: Converting old string list format to new object format");
                            foreach (var path in oldPaths)
                            {
                                try 
                                {
                                    var storageFile = await StorageFile.GetFileFromPathAsync(path);
                                    AddFile(storageFile, saveAfterAdd: false);
                                }
                                catch { }
                            }
                            SavePlaylist(); // 轉成新格式儲存
                        }
                        return;
                    }
                    catch 
                    {
                        System.Diagnostics.Debug.WriteLine("PlaylistWindow: Failed to deserialize playlist JSON (corrupted)");
                        return;
                    }
                }

                if (savedItems == null || savedItems.Count == 0) return;

                System.Diagnostics.Debug.WriteLine($"PlaylistWindow: Loading {savedItems.Count} items from {PlaylistFileName}");
                int loadedCount = 0;

                foreach (var item in savedItems)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) continue;

                    // 檢查是否已在清單中（避免重複）
                    bool exists = PlaylistListView.Items.Any(existing =>
                    {
                        return existing is PlaylistDisplayItem di && di.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase);
                    });

                    if (!exists)
                    {
                        PlaylistListView.Items.Add(item);
                        loadedCount++;
                    }
                }

                PlaylistListView.UpdateLayout();
                UpdateStatus?.Invoke($"播放清單：已載入 {loadedCount} 個檔案");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadPlaylistFromFileAsync error: {ex.Message}");
            }
            finally
            {
                _isLoadingPlaylist = false;
                // 載入後啟動背景時長更新，補齊可能是 Unknown 的項目
                StartDurationUpdate();
            }
        }
        // 視窗激活時載入播放清單（只載入一次）
        private bool _playlistLoaded = false;
        private async void PlaylistWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) return;

            if (!_playlistLoaded)
            {
                _playlistLoaded = true;
                await LoadPlaylistFromFileAsync();  // 改成新的方法
            }
        }
        
        // 儲存視窗狀態（位置和尺寸）
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
        
        // 當視窗關閉時保存播放清單和視窗狀態
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
            // 移除狀態訊息，避免拖拽時頻繁更新狀態
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
                        // 拖放後開始背景更新長度
                        StartDurationUpdate();
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
            
            // 嘗試從選中的項目獲取路徑
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
                // 如果沒有選中項，嘗試從點擊的元素查找
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

                    // 若檔案成功讀取，且原本被標示為遺失，則還原顯示
                    PlaylistDisplayItem successItem = null;
                    if (PlaylistListView.SelectedItem is PlaylistDisplayItem si && si.FullPath == path)
                    {
                        successItem = si;
                    }
                    else
                    {
                        foreach (var item in PlaylistListView.Items)
                        {
                            if (item is PlaylistDisplayItem di && di.FullPath == path)
                            {
                                successItem = di;
                                break;
                            }
                        }
                    }

                    if (successItem != null && successItem.IsMissing)
                    {
                        successItem.IsMissing = false;
                        int index = PlaylistListView.Items.IndexOf(successItem);
                        if (index >= 0)
                        {
                            PlaylistListView.Items[index] = successItem;
                        }
                    }
                    
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

                    // 將該項目標示為遺失（字體變紅）
                    // 找出是哪一個項目出錯
                    PlaylistDisplayItem targetItem = null;
                    if (PlaylistListView.SelectedItem is PlaylistDisplayItem si && si.FullPath == path)
                    {
                        targetItem = si;
                    }
                    else
                    {
                        // 遍歷找出路徑符合的項目
                        foreach (var item in PlaylistListView.Items)
                        {
                            if (item is PlaylistDisplayItem di && di.FullPath == path)
                            {
                                targetItem = di;
                                break;
                            }
                        }
                    }

                    if (targetItem != null)
                    {
                        targetItem.IsMissing = true;
                        
                        // 重新整理該項目的顯示
                        int index = PlaylistListView.Items.IndexOf(targetItem);
                        if (index >= 0)
                        {
                            PlaylistListView.Items[index] = targetItem; 
                        }
                    }
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
                // 輸出選中項目的所有欄位內容
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
            // 先找到被點擊的項目
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

            // 清除選單項
            var deleteItem = new MenuFlyoutItem { Text = "清除此項目" };
            deleteItem.Click += (s, args) => DeleteSelectedItems();
            flyout.Items.Add(deleteItem);

            // 重新命名選單項
            var renameItem = new MenuFlyoutItem { Text = "重新命名..." };
            renameItem.Click += async (s, args) => await RenameSelectedFile();
            flyout.Items.Add(renameItem);

            // 刪除實體檔案選單項（紅色）
            var deleteFileItem = new MenuFlyoutItem { Text = "刪除實體檔案..." };
            deleteFileItem.Click += async (s, args) => await DeleteSelectedFiles();
            // 設置紅色背景
            deleteFileItem.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            flyout.Items.Add(deleteFileItem);

            // 分隔線
            flyout.Items.Add(new MenuFlyoutSeparator());

            // 清除所有播放清單項目選單項
            var deleteAllItem = new MenuFlyoutItem { Text = "清除所有播放清單項目" };
            deleteAllItem.Click += async (s, args) => await DeleteAllItems();
            flyout.Items.Add(deleteAllItem);

            // 分隔線
            flyout.Items.Add(new MenuFlyoutSeparator());

            // 開啟檔案總管選單項
            var openExplorerItem = new MenuFlyoutItem { Text = "開啟檔案總管" };
            openExplorerItem.Click += (s, args) => OpenSelectedFileLocation();
            flyout.Items.Add(openExplorerItem);

            // 顯示在滑鼠游標右方
            flyout.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        }

        private void OpenSelectedFileLocation()
        {
            var selected = PlaylistListView.SelectedItem;
            if (selected == null) return;

            string path = null;
            if (selected is PlaylistDisplayItem displayItem)
            {
                path = displayItem.FullPath;
            }
            else if (selected is Grid row && row.Tag is string tagPath)
            {
                path = tagPath;
            }

            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // 使用 explorer.exe /select,"path" 來開啟資料夾並選中檔案
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PlaylistWindow - Open explorer error: {ex.Message}");
                }
            }
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
                UpdateStatus?.Invoke($"Playlist: 已清除 {count} 個項目");
                // 删除项目后保存播放清单
                SavePlaylist();
            }
        }
        
        // 清除所有播放清單項目
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
                Title = "確認清空所有項目",
                Content = $"確定要清空播放清單中的所有 {totalCount} 個項目嗎？",
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            
            // 清空播放清單
            PlaylistListView.Items.Clear();
            
            // 更新主窗口狀態列
            UpdateStatus?.Invoke($"Playlist: 已清空所有 {totalCount} 個項目");
            
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


        private bool _isLoadingPlaylist = false; // 標記是否正在載入播放清單
        private bool _isDeletingFiles = false; // 標記是否正在刪除檔案，防止在刪除過程中觸發自動播放

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
                var grid = args.ItemContainer.ContentTemplateRoot as Grid;
                if (grid != null)
                {
                    UpdateDisplayItemUI(item, grid);
                }
                args.Handled = true; // 告訴系統我們已經處理完畢
            }
        }
        
        // 統一更新項目 UI 的方法
        private void UpdateDisplayItemUI(PlaylistDisplayItem item, Grid grid)
        {
            if (item == null || grid == null) return;

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

            // 設定顏色：如果檔案遺失，顯示紅色
            var foreground = item.IsMissing ? new SolidColorBrush(Microsoft.UI.Colors.Red) : new SolidColorBrush(Microsoft.UI.Colors.Black);
            if (tb1 != null) tb1.Foreground = foreground;
            if (tb2 != null) tb2.Foreground = foreground;
            if (tb3 != null) tb3.Foreground = foreground;
            if (tb4 != null) tb4.Foreground = foreground;
            if (tb5 != null) tb5.Foreground = foreground;

            // Add ToolTip to show full path
            ToolTipService.SetToolTip(grid, item.FullPath);
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
                if (files != null && files.Count > 0)
                {
                    _isLoadingPlaylist = true;
                    foreach (var file in files)
                    {
                        AddFile(file, saveAfterAdd: false);
                    }
                    _isLoadingPlaylist = false;
                    SavePlaylist();
                    // 開放檔案後開始背景更新長度
                    StartDurationUpdate();
                    
                    UpdateStatus?.Invoke($"Playlist: 已添加 {files.Count} 個檔案");
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
                        // 目錄添加後開始背景更新長度
                        StartDurationUpdate();
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

                // 檢查檔案是否存在
                item.IsMissing = !System.IO.File.Exists(item.FullPath);

                // 強制這個容器載入模板（這行很重要！）
                container.UpdateLayout();

                // 使用統一的更新方法
                if (container.ContentTemplateRoot is Grid grid)
                {
                    UpdateDisplayItemUI(item, grid);
                }
            }
            
            // 刷新時也嘗試背景更新長度
            StartDurationUpdate();
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
                    Duration = duration,
                    ModifiedDate = modified,
                    Directory = directory,
                    FullPath = file.Path,
                    RawSize = (long)props.Size,
                    RawModifiedDate = props.DateModified.LocalDateTime,
                    RawDuration = TimeSpan.Zero // 可在後續擴展中填入真實長度
                };

                // Add the item to the ListView (will use ItemTemplate)
                PlaylistListView.Items.Add(displayItem);

               // 從 ListView 中讀取剛添加的項目，確認資料已正確儲存
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
                            UpdateDisplayItemUI(addedItem, grid);
                        }
                    }

                    // 輸出從播放清單項目中讀取的所有文字內容到調試輸出
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
        
        // 格式化檔案大小（類似Windows檔案總管的顯示方式，通常以KB為單位）
        private string FormatFileSize(ulong bytes)
        {
            // Windows檔案總管的顯示規則：
            // - 小於1KB：顯示為位元組（B）
            // - 1KB到1MB：顯示為KB，保留2位小數
            // - 1MB到1GB：顯示為MB，保留2位小數
            // - 1GB以上：顯示為GB，保留2位小數
            
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
        // 背景長度更新相關
        private CancellationTokenSource _durationUpdateCts;
        private bool _isUpdatingDuration = false;

        private void StartDurationUpdate()
        {
            // 取消之前的任務
            _durationUpdateCts?.Cancel();
            _durationUpdateCts = new CancellationTokenSource();
            var token = _durationUpdateCts.Token;

            // 在 UI 執行緒先抓取清單快照，避免跨執行緒存取錯誤
            List<PlaylistDisplayItem> itemsToUpdate;
            try
            {
                itemsToUpdate = PlaylistListView.Items.OfType<PlaylistDisplayItem>().ToList();
                System.Diagnostics.Debug.WriteLine($"StartDurationUpdate: Found {itemsToUpdate.Count} items to check.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartDurationUpdate UI items access error: {ex.Message}");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    // 延遲一下讓 UI 穩定
                    await Task.Delay(800, token);
                    if (token.IsCancellationRequested) return;

                    await UpdateDurationsInBackground(itemsToUpdate, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StartDurationUpdate error: {ex.Message}");
                }
            }, token);
        }

        private async Task UpdateDurationsInBackground(List<PlaylistDisplayItem> itemsToUpdate, CancellationToken token)
        {
            if (_isUpdatingDuration)
            {
                System.Diagnostics.Debug.WriteLine("UpdateDurations: Already updating, skipping...");
                return;
            }
            _isUpdatingDuration = true;
            int changeCount = 0;
            int processedCount = 0;

            try
            {
                System.Diagnostics.Debug.WriteLine($"UpdateDurations: Starting background update for {itemsToUpdate.Count} items...");

                foreach (var item in itemsToUpdate)
                {
                    if (token.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine("UpdateDurations: Task cancelled.");
                        break;
                    }

                    processedCount++;
                    bool itemChanged = false;

                    // 1. 檢查檔案是否存在
                    bool fileExists = System.IO.File.Exists(item.FullPath);
                    if (item.IsMissing != !fileExists) // 如果目前的 IsMissing 狀態跟實際存在狀態不符
                    {
                        item.IsMissing = !fileExists;
                        itemChanged = true;
                        System.Diagnostics.Debug.WriteLine($"UpdateDurations: File status changed [{item.FileName}] - IsMissing: {item.IsMissing}");
                    }

                    // 2. 如果檔案存在，嘗試更新長度（如果尚未獲取）
                    if (fileExists)
                    {
                        // 只有「未知」或「零」的才需要更新
                        //bool isUnknown = string.Equals(item.Duration, "Unknown", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(item.Duration);
                        //強制每個檔案都更新長度資料
                        bool isUnknown = true;
                        if (isUnknown || item.RawDuration == TimeSpan.Zero)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"UpdateDurations: Processing duration for [{item.FileName}]...");
                                var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                                var duration = await MediaHelper.GetMediaDurationAsync(file);

                                if (duration != TimeSpan.Zero)
                                {
                                    string durationStr = duration.TotalHours >= 1 
                                        ? duration.ToString(@"hh\:mm\:ss") 
                                        : duration.ToString(@"mm\:ss");

                                    item.Duration = durationStr;
                                    item.RawDuration = duration;
                                    itemChanged = true;
                                    System.Diagnostics.Debug.WriteLine($"UpdateDurations: Updated [{item.FileName}] -> {durationStr}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"UpdateDurations: Error getting duration for {item.FileName}: {ex.Message}");
                            }
                        }
                    }

                    // 3. 如果項目有變更，更新 UI
                    if (itemChanged)
                    {
                        changeCount++;
                        // 回到 UI 執行緒更新顯示
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            var container = PlaylistListView.ContainerFromItem(item) as ListViewItem;
                            if (container != null && container.ContentTemplateRoot is Grid grid)
                            {
                                UpdateDisplayItemUI(item, grid);
                            }
                        });
                    }

                    // 稍微間隔一下，避免搶佔資源
                    if (processedCount % 10 == 0) 
                    {
                        await Task.Delay(1, token);
                    }
                }
            }
            finally
            {
                _isUpdatingDuration = false;
                System.Diagnostics.Debug.WriteLine($"UpdateDurations: Finished. Changed {changeCount} items.");
                
                // 如果有更新到任何項目的長度或狀態，就存檔一次
                if (changeCount > 0)
                {
                    this.DispatcherQueue.TryEnqueue(() => SavePlaylist());
                }
            }
        }

        private async Task RenameSelectedFile()
        {
            var selectedItem = PlaylistListView.SelectedItem;
            if (selectedItem == null) return;

            string currentFilePath = null;
            string currentFileName = null;

            if (selectedItem is PlaylistDisplayItem displayItem)
            {
                currentFilePath = displayItem.FullPath;
                currentFileName = displayItem.FileName;
            }
            else if (selectedItem is Grid row && row.Tag is string tagPath)
            {
                currentFilePath = tagPath;
                currentFileName = System.IO.Path.GetFileName(currentFilePath);
            }

            if (string.IsNullOrEmpty(currentFilePath) || string.IsNullOrEmpty(currentFileName))
            {
                UpdateStatus?.Invoke("Playlist: 無法獲取檔案資訊");
                return;
            }

            // 創建輸入對話框
            var inputTextBox = new TextBox
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(currentFileName),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var dialog = new ContentDialog
            {
                Title = "重新命名檔案",
                Content = inputTextBox,
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };

            // 當對話框打開時，選中文字
            dialog.Loaded += (s, e) =>
            {
                inputTextBox.SelectAll();
                inputTextBox.Focus(FocusState.Programmatic);
            };

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            string newFileNameWithoutExt = inputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newFileNameWithoutExt))
            {
                UpdateStatus?.Invoke("Playlist: 檔案名稱不能為空");
                return;
            }

            // 檢查檔案名稱是否包含非法字元
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            if (newFileNameWithoutExt.IndexOfAny(invalidChars) >= 0)
            {
                UpdateStatus?.Invoke("Playlist: 檔案名稱包含非法字元");
                return;
            }

            // 獲取副檔名並組合新檔案名稱
            string extension = System.IO.Path.GetExtension(currentFileName);
            string newFileName = newFileNameWithoutExt + extension;
            string directory = System.IO.Path.GetDirectoryName(currentFilePath);
            string newFilePath = System.IO.Path.Combine(directory, newFileName);

            // 檢查新檔案名稱是否與原名稱相同
            if (string.Equals(currentFileName, newFileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 檢查目標檔案是否已存在
            if (System.IO.File.Exists(newFilePath))
            {
                UpdateStatus?.Invoke($"Playlist: 檔案 '{newFileName}' 已存在");
                return;
            }

            try
            {
                // 重新命名實體檔案
                System.IO.File.Move(currentFilePath, newFilePath);

                // 更新播放清單項目
                await UpdatePlaylistItemAfterRename(currentFilePath, newFilePath);

                // 觸發事件通知 MainWindow
                FileRenamed?.Invoke(currentFilePath, newFilePath);

                UpdateStatus?.Invoke($"Playlist: 檔案已重新命名為 '{newFileName}'");
            }
            catch (Exception ex)
            {
                UpdateStatus?.Invoke($"Playlist: 重新命名失敗 - {ex.Message}");
            }
        }

        private async Task UpdatePlaylistItemAfterRename(string oldPath, string newPath)
        {
            // 找到並更新播放清單中的項目
            for (int i = 0; i < PlaylistListView.Items.Count; i++)
            {
                var item = PlaylistListView.Items[i];
                if (item is PlaylistDisplayItem displayItem && displayItem.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    // 更新內容
                    displayItem.FileName = System.IO.Path.GetFileName(newPath);
                    displayItem.FullPath = newPath;
                    displayItem.Directory = System.IO.Path.GetDirectoryName(newPath) ?? string.Empty;

                    // 重新獲取一些屬性
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(newPath);
                        var props = await file.GetBasicPropertiesAsync();
                        displayItem.ModifiedDate = props.DateModified.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
                        displayItem.RawModifiedDate = props.DateModified.LocalDateTime;
                    }
                    catch { }

                    // 更新 UI 顯示
                    var container = PlaylistListView.ContainerFromIndex(i) as ListViewItem;
                    if (container != null && container.ContentTemplateRoot is Grid grid)
                    {
                        UpdateDisplayItemUI(displayItem, grid);
                    }
                    break;
                }
            }

            // 保存播放清單
            SavePlaylist();
        }
    }
}
