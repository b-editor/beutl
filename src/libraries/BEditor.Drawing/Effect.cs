// Effect.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using BEditor.Compute.Runtime;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;
using BEditor.LangResources;

using OpenCvSharp;

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
        /// <param name="args">The arguments.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void PixelOperate<TOperation>(this Image<BGRA32> image, DrawingContext context, params object[] args)
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
            var args1 = new object[args.Length + 1];
            for (var i = 0; i < args.Length; i++)
            {
                args1[i + 1] = args[i];
            }

            var dataSize = image.DataSize;
            using var buf = context.Context.CreateMappingMemory(image.Data, dataSize);
            args1[0] = buf;

            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, args1);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
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
        [Obsolete("Use PixelOperate<TOperation>.")]
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
        [Obsolete("Use PixelOperate<TOperation>.")]
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
        [Obsolete("Use PixelOperate<TOperation>.")]
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
        /// Start the specified pixel operation using the Gpu.
        /// </summary>
        /// <typeparam name="TOperation">The type of operation.</typeparam>
        /// <typeparam name="TArg1">The type of first argument.</typeparam>
        /// <typeparam name="TArg2">The type of second argument.</typeparam>
        /// <typeparam name="TArg3">The type of third argument.</typeparam>
        /// <typeparam name="TArg4">The type of fourth argument.</typeparam>
        /// <param name="image">The image to be operated.</param>
        /// <param name="context">A valid DrawingContext.</param>
        /// <param name="arg1">The first argument passed to the kernel.</param>
        /// <param name="arg2">The second argument passed to the kernel.</param>
        /// <param name="arg3">The third argument passed to the kernel.</param>
        /// <param name="arg4">The fourth argument passed to the kernel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        [Obsolete("Use PixelOperate<TOperation>.")]
        public static void PixelOperate<TOperation, TArg1, TArg2, TArg3, TArg4>(this Image<BGRA32> image, DrawingContext context, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
            where TOperation : struct, IGpuPixelOperation<TArg1, TArg2, TArg3, TArg4>
            where TArg1 : notnull
            where TArg2 : notnull
            where TArg3 : notnull
            where TArg4 : notnull
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
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, buf, arg1, arg2, arg3, arg4);
            context.CommandQueue.WaitFinish();
            buf.Read(context.CommandQueue, true, image.Data, 0, dataSize).Wait();
        }

        /// <summary>
        /// Borders the image.
        /// </summary>
        /// <param name="self">The image to be bordered.</param>
        /// <param name="size">The size of the border.</param>
        /// <param name="color">The color of the border.</param>
        /// <returns>Returns an image with <paramref name="self"/> bordered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="size"/> is less than 0.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static Image<BGRA32> Border(this Image<BGRA32> self, int size, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (size <= 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(size), 0));
            self.ThrowIfDisposed();

            var nwidth = self.Width + (size * 2);
            var nheight = self.Height + (size * 2);

            self = self.MakeBorder(nwidth, nheight);

            // アルファマップ
            using var alphamap = self.AlphaMap();
            using var alphaMat = alphamap.ToMat();

            // 縁
            var border = new Image<BGRA32>(self.Width, self.Height, default(BGRA32));
            using var borderMat = border.ToMat();

            // 輪郭検出
            alphaMat.FindContours(out var points, out var h, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            // 検出した輪郭を描画
            borderMat.DrawContours(points, -1, new(color.B, color.G, color.R, color.A), size, LineTypes.AntiAlias, h);

            self.Dispose();
            return border;
        }

        /// <summary>
        /// Blurs the edges of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        /// <param name="alphaEdge">If true, blurs the borders of transparency.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void EdgeBlur(this Image<BGRA32> image, Size kernelSize, bool alphaEdge)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            Image<BGRA32>? blurred;
            kernelSize = new(Math.Clamp(kernelSize.Width, 0, image.Width), Math.Clamp(kernelSize.Height, 0, image.Height));

            if (!alphaEdge)
            {
                var size = image.Size - kernelSize;
                blurred = new(size.Width, size.Height, Colors.White);
                var tmp = blurred.MakeBorder(image.Width, image.Height);
                blurred.Dispose();
                blurred = tmp;
            }
            else
            {
                blurred = image.Clone();
            }

            Cv.Blur(blurred, kernelSize);

            image.Mask(blurred, default, 0, false);
            blurred.Dispose();
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
            if (invert)
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

        /// <summary>
        /// Disassemble the parts in the image.
        /// </summary>
        /// <param name="image">The image of disassembling the parts.</param>
        /// <returns>Returns the decomposed image and its image rectangle.</returns>
        public static (Image<BGRA32>, Rectangle)[] PartsDisassembly(this Image<BGRA32> image)
        {
            using var alphamap = image.AlphaMap();
            using var alphaMat = alphamap.ToMat();

            // 輪郭検出
            alphaMat.FindContours(out var points, out var h, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            using var mask = new Image<BGRA32>(image.Width, image.Height, default(BGRA32));
            using var maskMat = mask.ToMat();

            var list = new List<(Image<BGRA32>, Rectangle)>();
            for (var i1 = 0; i1 < points.Length; i1++)
            {
                var p = points[i1];
                var x0 = image.Width;
                var y0 = image.Height;
                var x1 = 0;
                var y1 = 0;

                for (var i = 0; i < p.Length; i++)
                {
                    var x = p[i].X;
                    var y = p[i].Y;

                    if (x0 > x) x0 = x;
                    if (y0 > y) y0 = y;
                    if (x1 < x) x1 = x;
                    if (y1 < y) y1 = y;
                }

                mask.Clear();

                // 検出した輪郭を描画
                maskMat.DrawContours(points, i1, new Scalar(255, 255, 255, 255), -1, LineTypes.AntiAlias, h);

                var rect = Rectangle.FromLTRB(x0, y0, x1, y1);
                var partMask = mask[rect];
                var part = image[rect];

                part.AlphaSubtract(partMask);

                partMask.Dispose();
                list.Add((part, rect));
            }

            return list.ToArray();
        }

        /// <summary>
        /// Fill in the transparent areas.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="color">The color.</param>
        /// <returns>Returns an image with the transparent areas filled in.</returns>
        public static Image<BGRA32> FillTransparency(this Image<BGRA32> image, Color color)
        {
            using var alphamap = image.AlphaMap();
            using var alphaMat = alphamap.ToMat();

            // 輪郭検出
            alphaMat.FindContours(out var points, out var h, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            using var bmp = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                StrokeWidth = 10,
            };

            foreach (var p in points)
            {
                using var path1 = new SKPath();

                path1.MoveTo(p[0].X, p[0].Y);

                foreach (var item in p)
                {
                    path1.LineTo(item.X, item.Y);
                }

                path1.Close();

                canvas.DrawPath(path1, paint);
            }

            return bmp.ToImage32();
        }

        /// <summary>
        /// Applies a flat shadow effect.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="color">The color.</param>
        /// <param name="angle">The angle (degree).</param>
        /// <param name="length">The length.</param>
        /// <returns>Returns the image to which the effect has been applied.</returns>
        public static Image<BGRA32> FlatShadow(this Image<BGRA32> image, Color color, float angle, float length)
        {
            using var alphamap = image.AlphaMap();
            using var alphaMat = alphamap.ToMat();

            // 輪郭検出
            alphaMat.FindContours(out var points, out var h, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            var dgree = angle;
            var radian = dgree * (MathF.PI / 180);
            var x1 = MathF.Cos(radian);
            var y1 = MathF.Sin(radian);
            var x2 = (int)(length * MathF.Cos(radian));
            var y2 = (int)(length * MathF.Sin(radian));
            var x2Abs = Math.Abs(x2);
            var y2Abs = Math.Abs(y2);

            using var bmp = new SKBitmap(new SKImageInfo(image.Width + x2Abs, image.Height + y2Abs, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            foreach (var p in points)
            {
                canvas.Translate((x2Abs - x2) / 2, (y2Abs - y2) / 2);

                using var path = new SKPath();

                path.MoveTo(p[0].X, p[0].Y);

                for (var i = 0; i < p.Length; i++)
                {
                    var item = p[i];
                    path.LineTo(item.X, item.Y);
                }

                for (var i = 0; i < length; i++)
                {
                    canvas.Translate(x1, y1);
                    canvas.DrawPath(path, paint);
                }

                canvas.ResetMatrix();
            }

            using var srcBmp = image.ToSKBitmap();
            canvas.DrawBitmap(srcBmp, (x2Abs - x2) / 2, (y2Abs - y2) / 2);

            return bmp.ToImage32();
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