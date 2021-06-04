// Effect.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using BEditor.Compute.Runtime;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Starts the specified pixel operation.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <param name="size">The number of pixels to operate.</param>
        /// <param name="operation">The <see cref="IPixelOperation"/> to be invoked.</param>
        public static void PixelOperate<TOperation>(int size, in TOperation operation)
            where TOperation : IPixelOperation
        {
            Parallel.For(0, size, operation.Invoke);
        }

        /// <summary>
        /// Starts the specified multiple pixel operation.
        /// </summary>
        /// <typeparam name="TOperation1">The type of first operation.</typeparam>
        /// <typeparam name="TOperation2">The type of second operation.</typeparam>
        /// <param name="size">The number of pixels to operate.</param>
        /// <param name="process1">The first <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process2">The second <see cref="IPixelOperation"/> to be invoked.</param>
        public static void PixelOperate<TOperation1, TOperation2>(int size, TOperation1 process1, TOperation2 process2)
            where TOperation1 : IPixelOperation
            where TOperation2 : IPixelOperation
        {
            Parallel.For(0, size, i =>
            {
                process1.Invoke(i);
                process2.Invoke(i);
            });
        }

        /// <summary>
        /// Starts the specified multiple pixel operation.
        /// </summary>
        /// <typeparam name="TOperation1">The type of first operation.</typeparam>
        /// <typeparam name="TOperation2">The type of second operation.</typeparam>
        /// <typeparam name="TOperation3">The type of third operation.</typeparam>
        /// <param name="size">The number of pixels to operate.</param>
        /// <param name="process1">The first <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process2">The second <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process3">The third <see cref="IPixelOperation"/> to be invoked.</param>
        public static void PixelOperate<TOperation1, TOperation2, TOperation3>(int size, TOperation1 process1, TOperation2 process2, TOperation3 process3)
            where TOperation1 : IPixelOperation
            where TOperation2 : IPixelOperation
            where TOperation3 : IPixelOperation
        {
            Parallel.For(0, size, i =>
            {
                process1.Invoke(i);
                process2.Invoke(i);
                process3.Invoke(i);
            });
        }

        /// <summary>
        /// Starts the specified multiple pixel operation.
        /// </summary>
        /// <typeparam name="TOperation1">The type of first operation.</typeparam>
        /// <typeparam name="TOperation2">The type of second operation.</typeparam>
        /// <typeparam name="TOperation3">The type of third operation.</typeparam>
        /// <typeparam name="TOperation4">The type of fourth operation.</typeparam>
        /// <param name="size">The number of pixels to operate.</param>
        /// <param name="process1">The first <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process2">The second <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process3">The third <see cref="IPixelOperation"/> to be invoked.</param>
        /// <param name="process4">The fourth <see cref="IPixelOperation"/> to be invoked.</param>
        public static void PixelOperate<TOperation1, TOperation2, TOperation3, TOperation4>(int size, TOperation1 process1, TOperation2 process2, TOperation3 process3, TOperation4 process4)
            where TOperation1 : IPixelOperation
            where TOperation2 : IPixelOperation
            where TOperation3 : IPixelOperation
            where TOperation4 : IPixelOperation
        {
            Parallel.For(0, size, i =>
            {
                process1.Invoke(i);
                process2.Invoke(i);
                process3.Invoke(i);
                process4.Invoke(i);
            });
        }

        /// <summary>
        /// Start the specified pixel operation using the Gpu.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <param name="image">The image to be operated.</param>
        /// <param name="context">A valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void PixelOperate<TOperation>(this Image<BGRA32> image, DrawingContext context)
            where TOperation : struct, IGpuPixelOperation
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            CLProgram program;
            var operation = (TOperation)default;
            var key = operation.GetType().Name;
            if (!context.Programs.ContainsKey(key))
            {
                program = context.Context.CreateProgram(operation.GetSource());
                context.Programs.Add(key, program);
            }
            else
            {
                program = context.Programs[key];
            }

            using var kernel = program.CreateKernel(operation.GetKernel());

            var dataSize = image.DataSize;
            using var buf = context.Context.CreateMappingMemory(image.Data, dataSize);
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, buf);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
        }

        /// <summary>
        /// Start the specified pixel operation using the Gpu.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <typeparam name="TArg">The type of first argument.</typeparam>
        /// <param name="image">The image to be operated.</param>
        /// <param name="context">A valid DrawingContext.</param>
        /// <param name="arg">The first argument passed to the kernel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void PixelOperate<TOperation, TArg>(this Image<BGRA32> image, DrawingContext context, TArg arg)
            where TOperation : struct, IGpuPixelOperation<TArg>
            where TArg : notnull
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            CLProgram program;
            var operation = (TOperation)default;
            var key = operation.GetType().Name;
            if (!context.Programs.ContainsKey(key))
            {
                program = context.Context.CreateProgram(operation.GetSource());
                context.Programs.Add(key, program);
            }
            else
            {
                program = context.Programs[key];
            }

            using var kernel = program.CreateKernel(operation.GetKernel());

            var dataSize = image.DataSize;
            using var buf = context.Context.CreateMappingMemory(image.Data, dataSize);
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, buf, arg);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
        }

        /// <summary>
        /// Start the specified pixel operation using the Gpu.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <typeparam name="TArg1">The type of first argument.</typeparam>
        /// <typeparam name="TArg2">The type of second argument.</typeparam>
        /// <param name="image">The image to be operated.</param>
        /// <param name="context">A valid DrawingContext.</param>
        /// <param name="arg1">The first argument passed to the kernel.</param>
        /// <param name="arg2">The second argument passed to the kernel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void PixelOperate<TOperation, TArg1, TArg2>(this Image<BGRA32> image, DrawingContext context, TArg1 arg1, TArg2 arg2)
            where TOperation : struct, IGpuPixelOperation<TArg1, TArg2>
            where TArg1 : notnull
            where TArg2 : notnull
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            CLProgram program;
            var operation = (TOperation)default;
            var key = operation.GetType().Name;
            if (!context.Programs.ContainsKey(key))
            {
                program = context.Context.CreateProgram(operation.GetSource());
                context.Programs.Add(key, program);
            }
            else
            {
                program = context.Programs[key];
            }

            using var kernel = program.CreateKernel(operation.GetKernel());

            var dataSize = image.DataSize;
            using var buf = context.Context.CreateMappingMemory(image.Data, dataSize);
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, buf, arg1, arg2);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
        }

        /// <summary>
        /// Start the specified pixel operation using the Gpu.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <typeparam name="TArg1">The type of first argument.</typeparam>
        /// <typeparam name="TArg2">The type of second argument.</typeparam>
        /// <typeparam name="TArg3">The type of third argument.</typeparam>
        /// <param name="image">The image to be operated.</param>
        /// <param name="context">A valid DrawingContext.</param>
        /// <param name="arg1">The first argument passed to the kernel.</param>
        /// <param name="arg2">The second argument passed to the kernel.</param>
        /// <param name="arg3">The third argument passed to the kernel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void PixelOperate<TOperation, TArg1, TArg2, TArg3>(this Image<BGRA32> image, DrawingContext context, TArg1 arg1, TArg2 arg2, TArg3 arg3)
            where TOperation : struct, IGpuPixelOperation<TArg1, TArg2, TArg3>
            where TArg1 : notnull
            where TArg2 : notnull
            where TArg3 : notnull
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            CLProgram program;
            var operation = (TOperation)default;
            var key = operation.GetType().Name;
            if (!context.Programs.ContainsKey(key))
            {
                program = context.Context.CreateProgram(operation.GetSource());
                context.Programs.Add(key, program);
            }
            else
            {
                program = context.Programs[key];
            }

            using var kernel = program.CreateKernel(operation.GetKernel());

            var dataSize = image.DataSize;
            using var buf = context.Context.CreateMappingMemory(image.Data, dataSize);
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, buf, arg1, arg2, arg3);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
        }

        /// <summary>
        /// Makes the specified image a mask for the original image.
        /// </summary>
        /// <param name="self">The image to apply the effect to.</param>
        /// <param name="mask">The image to use as mask.</param>
        /// <param name="point">The position of the mask.</param>
        /// <param name="rotate">The rotation angle of the mask.</param>
        /// <param name="invert">The value of whether to invert the mask.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="mask"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Mask(this Image<BGRA32> self, Image<BGRA32> mask, PointF point, float rotate, bool invert, DrawingContext? context = null)
        {
            static SKBitmap MakeMask(Size size, Image<BGRA32> mask, PointF point, float rotate)
            {
                using var paint = new SKPaint();
                var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888));
                using var canvas = new SKCanvas(bmp);
                using var m = mask.ToSKBitmap();

                canvas.Translate(size.Width / 2, size.Height / 2);
                canvas.RotateDegrees(rotate);
                canvas.DrawBitmap(
                    m,
                    new SKPoint(
                        point.X - (mask.Width / 2F),
                        point.Y - (mask.Height / 2F)),
                    paint);

                return bmp;
            }

            if (self is null) throw new ArgumentNullException(nameof(self));
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            self.ThrowIfDisposed();
            mask.ThrowIfDisposed();

            // 回転した画像
            using var m = MakeMask(self.Size, mask, point, rotate);
            using var routed = m.ToImage32();
            if (!invert)
            {
                routed.ReverseOpacity(context);
            }

            self.AlphaSubtract(routed, context);
        }

        /// <summary>
        /// Sets the opacity of the image.
        /// </summary>
        /// <param name="image">The image to set the opacity.</param>
        /// <param name="opacity">The opacity of the <paramref name="image"/>. [range: 0.0-1.0].</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void SetOpacity(this Image<BGRA32> image, float opacity)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new SetOpacityOperation(data, opacity));
            }
        }

        /// <summary>
        /// Sets the color of the image.
        /// </summary>
        /// <param name="image">The image to set the color.</param>
        /// <param name="color">The color of the <paramref name="image"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void SetColor(this Image<BGRA32> image, BGRA32 color)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new SetColorOperation(data, color));
            }
        }

        /// <summary>
        /// Makes a specific color component of the image transparent.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="value">The threshold value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ColorKey(this Image<BGRA32> image, BGRA32 color, int value)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* s = image.Data)
            {
                PixelOperate(image.Data.Length, new ColorKeyOperation(s, s, color, value));
            }
        }

        /// <summary>
        /// Converts the <see cref="Image{T}"/>.
        /// </summary>
        /// <typeparam name="T1">The type of pixel before conversion.</typeparam>
        /// <typeparam name="T2">The type of pixel after conversion.</typeparam>
        /// <param name="image">The image to convert.</param>
        /// <returns>Returns the converted image.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static Image<T2> Convert<T1, T2>(this Image<T1> image)
            where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
            where T2 : unmanaged, IPixel<T2>
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            var dst = new Image<T2>(image.Width, image.Height, default(T2));

            fixed (T1* srcPtr = image.Data)
            fixed (T2* dstPtr = dst.Data)
            {
                PixelOperate(image.Data.Length, new ConvertToOperation<T1, T2>(srcPtr, dstPtr));
            }

            return dst;
        }

        /// <summary>
        /// Normalize the histogram.
        /// </summary>
        /// <param name="image">The image to normalize.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Normalize(this Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* raw = image.Data)
            {
                byte r_min = 255;
                byte g_min = 255;
                byte b_min = 255;
                byte r_max = 0;
                byte g_max = 0;
                byte b_max = 0;

                var data = (IntPtr)raw;
                for (var i = 0; i < image.Data.Length; i++)
                {
                    // 最小値
                    if (raw[i].R < r_min) r_min = raw[i].R;
                    if (raw[i].G < g_min) g_min = raw[i].G;
                    if (raw[i].B < b_min) b_min = raw[i].B;

                    // 最大値
                    if (raw[i].R > r_max) r_max = raw[i].R;
                    if (raw[i].G > g_max) g_max = raw[i].G;
                    if (raw[i].B > b_max) b_max = raw[i].B;
                }

                // 比率を求める
                var r_ratio = 255 / (r_max - r_min);
                var g_ratio = 255 / (g_max - g_min);
                var b_ratio = 255 / (b_max - b_min);

                Parallel.For(0, image.Data.Length, i =>
                {
                    var raw = (BGRA32*)data;

                    raw[i].R = (byte)Set255Round(r_ratio * Math.Max(raw[i].R - r_min, 0));
                    raw[i].G = (byte)Set255Round(g_ratio * Math.Max(raw[i].G - g_min, 0));
                    raw[i].B = (byte)Set255Round(b_ratio * Math.Max(raw[i].B - b_min, 0));
                });
            }
        }

        /// <summary>
        /// Applies a noise effect.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="value">The threshold value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Noise(this Image<BGRA32> image, byte value)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                var rand = new Random();
                PixelOperate(image.Data.Length, new NoiseOperation(data, data, value, rand));
            }
        }

        /// <summary>
        /// Applies a noise effect.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="value">The threshold value.</param>
        /// <param name="seed">The seed value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Noise(this Image<BGRA32> image, byte value, int seed)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                var rand = new Random(seed);
                PixelOperate(image.Data.Length, new NoiseOperation(data, data, value, rand));
            }
        }

        /// <summary>
        /// Applies a diffusion effect.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="value">The threshold value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Diffusion(this Image<BGRA32> image, byte value)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            value = Math.Clamp(value, (byte)0, (byte)30);

            fixed (BGRA32* data = image.Data)
            {
                var raw = (IntPtr)data;
                var rand = new Random();

                Parallel.For(0, image.Height, y =>
                {
                    Parallel.For(0, image.Width, x =>
                    {
                        var data = (BGRA32*)raw;

                        // 取得する座標
                        var dy = Math.Abs(y + rand.Next(-value, value));
                        var dx = Math.Abs(x + rand.Next(-value, value));
                        int sPos;
                        var dPos = (y * image.Width) + x;

                        // 範囲外はデフォルトの値を使用する
                        if ((dy >= image.Height - 1) || (dx >= image.Width - 1))
                        {
                            sPos = dPos;
                        }
                        else
                        {
                            var sRow = dy * image.Width;
                            sPos = sRow + dx;
                        }

                        data[dPos].R = data[sPos].R;
                        data[dPos].G = data[sPos].G;
                        data[dPos].B = data[sPos].B;
                    });
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Set255(double value)
        {
            return value switch
            {
                > 255 => 255,
                < 0 => 0,
                _ => value,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float Set255(float value)
        {
            return value switch
            {
                > 255 => 255,
                < 0 => 0,
                _ => value,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Set255Round(double value)
        {
            return value switch
            {
                > 255 => 255,
                < 0 => 0,
                _ => Math.Round(value),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float Set255Round(float value)
        {
            return value switch
            {
                > 255 => 255,
                < 0 => 0,
                _ => MathF.Round(value),
            };
        }
    }
}