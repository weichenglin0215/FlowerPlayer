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
            
            // Set window size
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(400, 500));
        }
    }
}
