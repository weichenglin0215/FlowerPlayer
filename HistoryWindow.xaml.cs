using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using FlowerPlayer.Models;
using FlowerPlayer.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System;
using Windows.Storage;

namespace FlowerPlayer
{
    public sealed partial class HistoryWindow : Window
    {
        // Action to report status back to MainWindow
        public Action<string> UpdateStatus { get; set; }
        private readonly IMediaService _mediaService;

        public HistoryWindow(IMediaService mediaService)
        {
            _mediaService = mediaService;
            
            this.InitializeComponent();
            
            // Set window size
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 600));
            }
            catch { }

            // Load history from settings
            LoadHistory();
        }

        // ---------------------------------------------------------------------
        // Double‑tap a row to play the file in the main window (referenced as HistoryListView_DoubleTapped)
        // ---------------------------------------------------------------------
        public Action<StorageFile> OpenFileAction { get; set; }

        // ---------------------------------------------------------------------
        // Double‑tap a row to play the file in the main window (referenced as HistoryListView_DoubleTapped)
        // ---------------------------------------------------------------------
        private async void HistoryListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
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
                    UpdateStatus?.Invoke($"歷史清單: 正在播放 {file.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HistoryWindow - Play error: {ex.Message}");
                    UpdateStatus?.Invoke($"歷史清單: 播放錯誤 - {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------------
        // Right‑click context menu – delete selected items (referenced as HistoryListView_RightTapped)
        // ---------------------------------------------------------------------
        private void HistoryListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
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
            
            // 顯示在滑鼠游標右方
            flyout.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        }

        private void DeleteSelectedItems()
        {
            var selected = new List<object>(HistoryListView.SelectedItems);
            int count = selected.Count;
            var pathsToRemove = new List<string>();

            foreach (var item in selected)
            {
                if (item is Grid row && row.Tag is string path)
                {
                    pathsToRemove.Add(path);
                    HistoryListView.Items.Remove(item);
                }
            }

            // 從設置中移除這些路徑
            if (pathsToRemove.Count > 0)
            {
                var history = LocalSettingsService.HistoryPaths;
                foreach (var path in pathsToRemove)
                {
                    history.Remove(path);
                }
                LocalSettingsService.HistoryPaths = history;
            }
            
            // 更新主窗口狀態列
            if (count > 0)
            {
                UpdateStatus?.Invoke($"歷史清單: 已刪除 {count} 個項目");
            }
        }

        private async System.Threading.Tasks.Task DeleteSelectedFiles()
        {
            var selected = new List<object>(HistoryListView.SelectedItems);
            if (selected.Count == 0) return;

            // Build file list string for dialog
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

            // 顯示確認對話框
            var dialog = new ContentDialog
            {
                Title = "確認刪除實體檔案",
                Content = $"確定要將以下 {selected.Count} 個檔案移至資源回收桶嗎？\n\n{fileNames}",
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // Check if current file is in selected list
            string currentPath = _mediaService.CurrentFile?.Path;
            bool isPlayingSelected = false;

            if (!string.IsNullOrEmpty(currentPath))
            {
                foreach (var item in selected)
                {
                     if (item is Grid row && row.Tag is string path && path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                     {
                         isPlayingSelected = true;
                         break;
                     }
                }
            }

            // If playing file is selected, handle stop and close
            if (isPlayingSelected)
            {
                // Stop and Close to release handle
                _mediaService.Close();
                // History window doesn't support sequential play logic as strictly as playlist, 
                // but we could try to play next if we wanted. For now, just stop.
            }

            // 刪除檔案
            int deletedCount = 0;
            var itemsToRemove = new List<object>();
            var pathsToRemove = new List<string>();

            foreach (var item in selected)
            {
                if (item is Grid row && row.Tag is string path)
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        await file.DeleteAsync(StorageDeleteOption.Default);
                        itemsToRemove.Add(item);
                        pathsToRemove.Add(path);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"HistoryWindow - Delete file error: {ex.Message}");
                    }
                }
            }

            // 從列表中移除
            foreach (var item in itemsToRemove)
            {
                HistoryListView.Items.Remove(item);
            }

            // 從設置中移除這些路徑
            if (pathsToRemove.Count > 0)
            {
                var history = LocalSettingsService.HistoryPaths;
                foreach (var path in pathsToRemove)
                {
                    history.Remove(path);
                }
                LocalSettingsService.HistoryPaths = history;
            }

            // 更新主窗口狀態列
            if (deletedCount > 0)
            {
                UpdateStatus?.Invoke($"歷史清單: 已刪除 {deletedCount} 個實體檔案");
            }
        }

        // 載入歷史記錄
        private void LoadHistory()
        {
            try
            {
                var historyPaths = LocalSettingsService.HistoryPaths;
                HistoryListView.Items.Clear();

                var pathsToRemove = new List<string>();

                foreach (var path in historyPaths)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(path)) continue;
                        
                        // 使用同步方式获取文件（与播放清单相同的方式）
                        var file = StorageFile.GetFileFromPathAsync(path).AsTask().Result;
                        AddFile(file);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"HistoryWindow - Load error for {path}: {ex.Message}");
                        pathsToRemove.Add(path);
                    }
                }

                // 移除无效路径
                if (pathsToRemove.Count > 0)
                {
                    var history = LocalSettingsService.HistoryPaths;
                    foreach (var path in pathsToRemove)
                    {
                        history.Remove(path);
                    }
                    LocalSettingsService.HistoryPaths = history;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HistoryWindow - LoadHistory error: {ex.Message}");
            }
        }

        public void AddFile(Windows.Storage.StorageFile file)
        {
            try
            {
                var props = file.GetBasicPropertiesAsync().AsTask().Result;
                var directory = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
                var duration = "Unknown"; // Placeholder, could be calculated later
                var modified = props.DateModified.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

                // Create a Grid representing a row with four columns
                var rowGrid = new Grid();
                // Column definitions must match the header widths
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(400) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(150) });
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

                // Duration
                var tbDur = new TextBlock
                {
                    Text = duration,
                    Margin = new Thickness(5, 2, 5, 2)
                };
                Grid.SetColumn(tbDur, 1);
                rowGrid.Children.Add(tbDur);

                // Modified date
                var tbMod = new TextBlock
                {
                    Text = modified,
                    Margin = new Thickness(5, 2, 5, 2)
                };
                Grid.SetColumn(tbMod, 2);
                rowGrid.Children.Add(tbMod);

                // Directory
                var tbDir = new TextBlock
                {
                    Text = directory,
                    Margin = new Thickness(5, 2, 5, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(tbDir, 3);
                rowGrid.Children.Add(tbDir);

                // 將文件路徑存儲在 Tag 中，以便雙擊時可以獲取
                rowGrid.Tag = file.Path;

                // Add the row to the ListView
                HistoryListView.Items.Add(rowGrid);
                System.Diagnostics.Debug.WriteLine($"Added to history: {file.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding file to history: {ex.Message}");
            }
        }

        // 刷新歷史清單顯示
        public void Refresh()
        {
            LoadHistory();
        }
    }
}
