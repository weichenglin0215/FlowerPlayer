using System;
using System.Runtime.InteropServices;

namespace FlowerPlayer.Helpers
{
    public static class WindowHelper
    {
        [ComImport]
        [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IInitializeWithWindow
        {
            void Initialize(IntPtr hwnd);
        }

        public static void InitializeWithWindow(object target, IntPtr hwnd)
        {
            var initWindow = target as IInitializeWithWindow;
            if (initWindow != null)
            {
                initWindow.Initialize(hwnd);
            }
            else
            {
                // Fallback for types that might not implement the interface directly in the expected way,
                // though for FileOpenPicker/FileSavePicker in WinUI 3, the cast above usually works.
                // In some CsWinRT versions, we might need to rely on the WinRT.Interop one, 
                // but since that failed, we assume this direct COM cast is the safer bet.
                throw new InvalidCastException("Target does not implement IInitializeWithWindow");
            }
        }
    }
}
