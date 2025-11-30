using System;
using Microsoft.UI.Xaml;
using FlowerPlayer.ViewModels;

namespace FlowerPlayer
{
    public sealed partial class SettingsWindow : Window
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsWindow()
        {
            this.InitializeComponent();
            ViewModel = new SettingsViewModel();
            // Set DataContext for x:Bind to work
            RootGrid.DataContext = ViewModel;
            
            // 恢復窗口位置和尺寸
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                // 恢復尺寸
                var savedSize = Services.LocalSettingsService.GetWindowSize(Services.LocalSettingsService.KeySettingsWindowSize);
                if (savedSize.HasValue)
                {
                    appWindow.Resize(savedSize.Value);
                }
                else
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(400, 500));
                }
                
                // 恢復位置
                var savedPosition = Services.LocalSettingsService.GetWindowPosition(Services.LocalSettingsService.KeySettingsWindowPosition);
                if (savedPosition.HasValue)
                {
                    appWindow.Move(savedPosition.Value);
                }
                
                // 監聽位置和尺寸變化
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPositionChange || args.DidSizeChange)
                    {
                        SaveWindowState();
                    }
                };
            }
            catch { }
            
            // 註冊窗口關閉事件，保存窗口狀態
            this.Closed += SettingsWindow_Closed;
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
                    Services.LocalSettingsService.KeySettingsWindowPosition, 
                    appWindow.Position);
                Services.LocalSettingsService.SaveWindowSize(
                    Services.LocalSettingsService.KeySettingsWindowSize, 
                    appWindow.Size);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsWindow.SaveWindowState error: {ex.Message}");
            }
        }
        
        // 窗口關閉時保存窗口狀態
        private void SettingsWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowState();
        }
    }
}
