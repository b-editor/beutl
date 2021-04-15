using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace BEditor.Models.ColorTool
{
    public unsafe class ColorDropper
    {
        public static void Run(Action<System.Windows.Media.Color> action) => new ColorDropper(action).Start();

        private readonly DispatcherTimer timer = new(DispatcherPriority.Normal);
        private readonly Action<System.Windows.Media.Color> Action;

        public ColorDropper(Action<System.Windows.Media.Color> action)
        {
            Action = action;
            timer.Interval = new TimeSpan(0, 0, 0, 0, 10);
        }

        public void Start()
        {
            timer.Start();
            timer.Tick += Timer_Tick;
        }

        //クリックされているか判定用
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtkey);
        [DllImport("user32.dll")]
        private static extern int GetCursorPos(POINT* point);
        //クリック判定
        private static bool IsClickDown => GetKeyState(0x01) < 0;

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (IsClickDown)
            {
                timer.Stop();
                timer.Tick -= Timer_Tick;

                POINT pos = default;

                if (GetCursorPos(&pos) is 1)
                {
                    var col = ColorSet(pos.X, pos.Y);

                    Action(col);
                }
            }
        }

        private static unsafe System.Windows.Media.Color ColorSet(double X, double Y)
        {
            var bitmap = new Bitmap((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, PixelFormat.Format32bppArgb);

            using var bmpGraphics = System.Drawing.Graphics.FromImage(bitmap);
            bmpGraphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            var color = bitmap.GetPixel((int)X, (int)Y);

            return System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);
        }

        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}