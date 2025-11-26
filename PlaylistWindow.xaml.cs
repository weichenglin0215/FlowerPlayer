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

        public PlaylistWindow(IMediaService mediaService, ViewModels.PlaylistViewModel existingViewModel = null)
        {
            _mediaService = mediaService;
            ViewModel = existingViewModel ?? new PlaylistViewModel(mediaService);
            
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
        }

        // ---------------------------------------------------------------------
        // Drag‑over handler for the whole window (referenced in XAML as Playlist_DragOver)
        // ---------------------------------------------------------------------
        private void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            UpdateStatus?.Invoke("Playlist: DragOver");
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
                    _mediaService.Open(file);
                    _mediaService.Play();
                    
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
            }
        }

        private async System.Threading.Tasks.Task DeleteSelectedFiles()
        {
            var selected = new List<object>(PlaylistListView.SelectedItems);
            if (selected.Count == 0) return;

            // 顯示確認對話框
            var dialog = new ContentDialog
            {
                Title = "確認刪除",
                Content = $"確定要將選取的 {selected.Count} 個檔案移至資源回收桶嗎？",
                PrimaryButtonText = "確定",
                SecondaryButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // 刪除檔案
            int deletedCount = 0;
            var itemsToRemove = new List<object>();

            foreach (var item in selected)
            {
                if (item is Grid row && row.Tag is string path)
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        await file.DeleteAsync(StorageDeleteOption.Default);
                        itemsToRemove.Add(item);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PlaylistWindow - Delete file error: {ex.Message}");
                    }
                }
            }

            // 從列表中移除
            foreach (var item in itemsToRemove)
            {
                PlaylistListView.Items.Remove(item);
            }

            // 更新主窗口狀態列
            if (deletedCount > 0)
            {
                UpdateStatus?.Invoke($"Playlist: 已刪除 {deletedCount} 個實體檔案");
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
                PlaylistListView.Items.Add(rowGrid);
                System.Diagnostics.Debug.WriteLine($"Added to playlist: {file.Name}");
                UpdateStatus?.Invoke($"Playlist: Added {file.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding file to playlist: {ex.Message}");
            }
        }

        // 獲取下一個文件路徑（用於自動播放）
        public string? GetNextFilePath(string? currentPath)
        {
            bool foundCurrent = false;
            System.Diagnostics.Debug.WriteLine($"GetNextFilePath: Looking for next file after {currentPath}, Total items: {PlaylistListView.Items.Count}");
            
            foreach (var item in PlaylistListView.Items)
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
