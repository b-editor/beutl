using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;

using Image = BEditor.Core.Media.Image;

namespace BEditor.Models.Extension {
    public static class BitmapSourceConverter {
        public static Image ToImage(this BitmapSource src) {
            var img = new Image((int)src.Width, (int)src.Height);
            ToImage(src, img);
            return img;
        }

        public static BitmapSource ToBitmapSource(this Image src) {
            var Bitmap = new WriteableBitmap(src.Width, src.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            ToWriteableBitmap(src, Bitmap);
            return Bitmap;
        }

        public static void ToWriteableBitmap(Image src, WriteableBitmap dst) {
            if (src == null) {
                throw new ArgumentNullException(nameof(src));
            }

            if (dst == null) {
                throw new ArgumentNullException(nameof(dst));
            }

            if (src.Width != dst.PixelWidth || src.Height != dst.PixelHeight) {
                throw new ArgumentException("size of src must be equal to size of dst");
            }

            int w = src.Width;
            int h = src.Height;
            int bpp = dst.Format.BitsPerPixel;


            bool submat = src.IsSubmatrix;
            bool continuous = src.IsContinuous;
            unsafe {
                byte* pSrc = (byte*)(src.Data);
                int sstep = (int)src.Step;

                if (bpp == 1) {
                    if (submat) {
                        throw new NotImplementedException("submatrix not supported");
                    }

                    // 手作業で移し替える
                    int stride = w / 8 + 1;
                    if (stride < 2) {
                        stride = 2;
                    }

                    byte[] pixels = new byte[h * stride];

                    for (int x = 0, y = 0; y < h; y++) {
                        int offset = y * stride;
                        for (int bytePos = 0; bytePos < stride; bytePos++) {
                            if (x < w) {
                                byte b = 0;
                                // 現在の位置から横8ピクセル分、ビットがそれぞれ立っているか調べ、1つのbyteにまとめる
                                for (int i = 0; i < 8; i++) {
                                    b <<= 1;
                                    if (x < w && pSrc[sstep * y + x] != 0) {
                                        b |= 1;
                                    }
                                    x++;
                                }
                                pixels[offset + bytePos] = b;
                            }
                        }
                        x = 0;
                    }
                    dst.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    return;
                }

                // 一気にコピー            
                if (!submat && continuous) {
                    long imageSize = src.DataEnd.ToInt64() - src.Data.ToInt64();
                    if (imageSize < 0) {
                        throw new Exception("The mat has invalid data pointer");
                    }

                    if (imageSize > int.MaxValue) {
                        throw new Exception("Too big mat data");
                    }

                    dst.WritePixels(new Int32Rect(0, 0, w, h), src.Data, (int)imageSize, sstep);
                    return;
                }

                // 一列ごとにコピー
                try {
                    dst.Lock();
                    dst.AddDirtyRect(new Int32Rect(0, 0, dst.PixelWidth, dst.PixelHeight));

                    int dstep = dst.BackBufferStride;
                    byte* pDst = (byte*)dst.BackBuffer;

                    for (int y = 0; y < h; y++) {
                        long offsetSrc = (y * sstep);
                        long offsetDst = (y * dstep);
                        CopyMemory(pDst + offsetDst, pSrc + offsetSrc, w * 4);
                    }
                }
                finally {
                    dst.Unlock();
                }
            }
        }
        private unsafe static void CopyMemory(void* outDest, void* inSrc, int inNumOfBytes) => Buffer.MemoryCopy(inSrc, outDest, inNumOfBytes, inNumOfBytes);

        public static void ToImage(this BitmapSource src, Image dst) {
            if (src == null) {
                throw new ArgumentNullException(nameof(src));
            }

            if (dst == null) {
                throw new ArgumentNullException(nameof(dst));
            }

            if (src.PixelWidth != dst.Width || src.PixelHeight != dst.Height) {
                throw new ArgumentException("size of src must be equal to size of dst");
            }

            int w = src.PixelWidth;
            int h = src.PixelHeight;
            int bpp = src.Format.BitsPerPixel;

            bool submat = dst.IsSubmatrix;
            bool continuous = dst.IsContinuous;

            unsafe {
                byte* p = (byte*)(dst.Data);
                long step = dst.Step;

                // 1bppは手作業でコピー
                if (bpp == 1) {
                    if (submat) {
                        throw new NotImplementedException("submatrix not supported");
                    }

                    // BitmapImageのデータを配列にコピー
                    // 要素1つに横8ピクセル分のデータが入っている。   
                    int stride = (w / 8) + 1;
                    byte[] pixels = new byte[h * stride];
                    src.CopyPixels(pixels, stride, 0);
                    int x = 0;

                    for (int y = 0; y < h; y++) {
                        int offset = y * stride;
                        // この行の各バイトを調べていく
                        for (int bytePos = 0; bytePos < stride; bytePos++) {
                            if (x < w) {
                                // 現在の位置のバイトからそれぞれのビット8つを取り出す
                                byte b = pixels[offset + bytePos];
                                for (int i = 0; i < 8; i++) {
                                    if (x >= w) {
                                        break;
                                    }
                                    p[step * y + x] = ((b & 0x80) == 0x80) ? (byte)255 : (byte)0;
                                    b <<= 1;
                                    x++;
                                }
                            }
                        }
                        // 次の行へ
                        x = 0;
                    }

                }
                else {
                    int stride = w * ((bpp + 7) / 8);
                    if (!submat && continuous) {
                        long imageSize = dst.DataEnd.ToInt64() - dst.Data.ToInt64();
                        if (imageSize < 0) {
                            throw new Exception("The mat has invalid data pointer");
                        }

                        if (imageSize > int.MaxValue) {
                            throw new Exception("Too big mat data");
                        }

                        src.CopyPixels(Int32Rect.Empty, dst.Data, (int)imageSize, stride);
                    }
                    else {
                        // 高さ1pxの矩形ごと(≒1行ごと)にコピー
                        var roi = new Int32Rect { X = 0, Y = 0, Width = w, Height = 1 };
                        IntPtr dstData = dst.Data;
                        for (int y = 0; y < h; y++) {
                            roi.Y = y;
                            src.CopyPixels(roi, dstData, stride, stride);
                            dstData = new IntPtr(dstData.ToInt64() + stride);
                        }
                    }
                }

            }
        }

        public static Bitmap ToBitmap(this BitmapSource src) {
            var bitmap = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppPArgb);
            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            src.CopyPixels(Int32Rect.Empty, bitmapData.Scan0, bitmapData.Height * bitmapData.Stride, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);

            return bitmap;
        }
    }
}
