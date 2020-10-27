using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BEditor.Models.Extension;

namespace BEditor.Models.ColorTool {
    public class ColorDropper {
        public static void Run(Action<System.Windows.Media.Color> action) => new ColorDropper(action).Start();

        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Normal);
        private readonly Action<System.Windows.Media.Color> Action;

        public ColorDropper(Action<System.Windows.Media.Color> action) {
            Action = action;
            timer.Interval = new TimeSpan(0, 0, 0, 0, 10);
        }

        public void Start() {
            timer.Start();
            timer.Tick += Timer_Tick;
        }

        //クリックされているか判定用
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtkey);
        //クリック判定
        private bool IsClickDown => GetKeyState(0x01) < 0;

        private void Timer_Tick(object sender, EventArgs e) {
            if (IsClickDown) {
                timer.Stop();
                timer.Tick -= Timer_Tick;

                var col = ColorSet(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);

                Action(col);
            }
        }

        private System.Windows.Media.Color ColorSet(double X, double Y) {
            Bitmap bitmap = new Bitmap((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var bmpGraphics = Graphics.FromImage(bitmap)) {
                bmpGraphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions()).ToBitmap();
            }

            PixelFormat pixelFormat = PixelFormat.Format32bppArgb;
            int pixelSize = 4;
            BitmapData bmpData = bitmap.LockBits(
              new Rectangle(0, 0, bitmap.Width, bitmap.Height),
              ImageLockMode.ReadOnly,
              pixelFormat
            );

            if (bmpData.Stride < 0) {
                bitmap.UnlockBits(bmpData);
                return new System.Windows.Media.Color();
            }

            IntPtr ptr = bmpData.Scan0;
            byte[] pixels = new byte[bmpData.Stride * bitmap.Height];
            Marshal.Copy(ptr, pixels, 0, pixels.Length);


            //X,Yのデータ位置
            int pos = (int)Y * bmpData.Stride + (int)X * pixelSize;
            // BGR
            var B = pixels[pos];
            var G = pixels[pos + 1];
            var R = pixels[pos + 2];

            bitmap.UnlockBits(bmpData);
            bitmap.Dispose();

            return System.Windows.Media.Color.FromRgb(R, G, B);
        }
    }
}
