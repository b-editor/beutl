using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;
using System.Windows.Interop;

namespace BEditor.Models
{
    public static class Clipboard
    {
        static Clipboard()
        {
            clipboardWatcher = new ClipboardWatcher(new WindowInteropHelper(App.Current.MainWindow).Handle);
            clipboardWatcher.DrawClipboard += (sender, e) =>
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    SetData(Data = System.Windows.Forms.Clipboard.GetText());
                }
                else if (System.Windows.Forms.Clipboard.ContainsFileDropList())
                {
                    SetData(System.Windows.Forms.Clipboard.GetFileDropList());
                }
            };
        }

        public static ClipboardWatcher clipboardWatcher = null;
        public static object Data;

        public static void SetData(object data)
        {
            Data = data;
        }

        public static object GetData() => Data;
    }

    public class ClipboardWatcher : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardViewer(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool ChangeClipboardChain(IntPtr hwnd, IntPtr hWndNext);

        const int WM_DRAWCLIPBOARD = 0x0308;
        const int WM_CHANGECBCHAIN = 0x030D;

        IntPtr nextHandle;
        readonly IntPtr handle;
        readonly HwndSource hwndSource = null;

        /// <summary>
        /// クリップボードに内容に変更があると発生
        /// </summary>
        public event EventHandler DrawClipboard;


        public ClipboardWatcher(IntPtr handle)
        {
            hwndSource = HwndSource.FromHwnd(handle);
            hwndSource.AddHook(WndProc);
            this.handle = handle;
            nextHandle = SetClipboardViewer(this.handle);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DRAWCLIPBOARD)
            {
                SendMessage(nextHandle, msg, wParam, lParam);
                RaiseDrawClipboard();
                handled = true;
            }
            else if (msg == WM_CHANGECBCHAIN)
            {
                if (wParam == nextHandle)
                {
                    nextHandle = lParam;
                }
                else
                {
                    SendMessage(nextHandle, msg, wParam, lParam);
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void RaiseDrawClipboard()
        {
            DrawClipboard?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// ClipBoardWatcherクラスを
        /// クリップボードビューアチェインから削除します。
        /// </summary>
        public void Dispose()
        {
            ChangeClipboardChain(handle, nextHandle);
            hwndSource.Dispose();
        }
    }
}
