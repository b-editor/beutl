// SceneCache.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the scene cache.
    /// </summary>
    public class SceneCache : IDisposable
    {
        private readonly Scene _scene;
        private IPreview? _preview;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneCache"/> class.
        /// </summary>
        /// <param name="scene">The scene.</param>
        public SceneCache(Scene scene)
        {
            _scene = scene;
        }

        /// <summary>
        /// Occurs when the cache is updated.
        /// </summary>
        public event EventHandler? Updated;

        /// <summary>
        /// Occurs when the cache is building.
        /// </summary>
        public event EventHandler<Range>? Building;

        private interface IPreview : IDisposable
        {
            public event EventHandler<Range>? Building;

            public Frame Start { get; }

            public Frame Length { get; }

            public bool LowColor { get; }

            public bool LowResolution { get; }

            public void Build();

            public Image<BGRA32> ReadImage(Frame frame);
        }

        /// <summary>
        /// Gets the width of the cache.
        /// </summary>
        public int Width => _scene.Width;

        /// <summary>
        /// Gets the height of the cache.
        /// </summary>
        public int Height => _scene.Height;

        /// <summary>
        /// Gets the starting position of the cache.
        /// </summary>
        public Frame Start => _preview?.Start ?? default;

        /// <summary>
        /// Gets the length of the cache.
        /// </summary>
        public Frame Length => _preview?.Length ?? default;

        /// <summary>
        /// Gets a value indicating whether the color reproduction is reduced.
        /// </summary>
        public bool LowColor => _preview?.LowColor ?? default;

        /// <summary>
        /// Gets a value indicating whether or not to lower the resolution.
        /// </summary>
        public bool LowResolution => _preview?.LowResolution ?? default;

        /// <inheritdoc/>
        public void Dispose()
        {
            Clear();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Creates a cache for the specified range.
        /// </summary>
        /// <param name="start">The starting position of the cache.</param>
        /// <param name="length">The length of the cache.</param>
        /// <param name="lowColor">Whether the color reproduction is reduced.</param>
        /// <param name="lowResolution">Whether or not to lower the resolution.</param>
        public void Create(Frame start, Frame length, bool lowColor = false, bool lowResolution = false)
        {
            Clear();

            var preview = new RamPreview(_scene, start, length, lowColor, lowResolution);
            preview.Building += Preview_Building;
            preview.Build();
            _preview = preview;

            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Creates a cache for the specified range.
        /// </summary>
        /// <param name="start">The starting position of the cache.</param>
        /// <param name="length">The length of the cache.</param>
        /// <param name="lowColor">Whether the color reproduction is reduced.</param>
        /// <param name="lowResolution">Whether or not to lower the resolution.</param>
        public void CreateStorage(Frame start, Frame length, bool lowColor = false, bool lowResolution = false)
        {
            Clear();
            var preview = new StoragePreview(_scene, start, length, lowColor, lowResolution);
            preview.Building += Preview_Building;
            preview.Build();
            _preview = preview;

            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void Clear()
        {
            if (_preview != null)
            {
                _preview.Building -= Preview_Building;
                _preview?.Dispose();
                _preview = null;
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Read the image from the cache.
        /// </summary>
        /// <param name="frame">The frame to read.</param>
        /// <returns>Returns the image that was read.</returns>
        public Image<BGRA32> ReadImage(Frame frame)
        {
            return _preview?.ReadImage(frame) ?? new Image<BGRA32>(Width, Height, (BGRA32)default);
        }

        private void Preview_Building(object? sender, Range e)
        {
            Building?.Invoke(this, e);
        }

        private class RamPreview : IPreview
        {
            private readonly Scene _scene;
            private Image<Bgra4444>[]? _bgra16;
            private Image<BGRA32>[]? _bgra32;

            public RamPreview(Scene scene, Frame start, Frame length, bool lowColor, bool lowResolution)
            {
                _scene = scene;
                Start = start;
                Length = length;
                LowColor = lowColor;
                LowResolution = lowResolution;
            }

            public event EventHandler<Range>? Building;

            public Frame Start { get; }

            public Frame Length { get; }

            public bool LowColor { get; }

            public bool LowResolution { get; }

            public void Build()
            {
                if (LowColor)
                {
                    _bgra16 = Enumerable.Range(Start.Value, Length.Value).Select(i =>
                    {
                        Building?.Invoke(this, new Range(Start.Value, i));
                        using var bgra32 = _scene.Render(i, ApplyType.Video);

                        if (LowResolution)
                        {
                            using var resized = bgra32.Resize(bgra32.Width / 2, bgra32.Height / 2, Quality.High);
                            return resized.Convert<Bgra4444>();
                        }
                        else
                        {
                            return bgra32.Convert<Bgra4444>();
                        }
                    }).ToArray();
                }
                else
                {
                    _bgra32 = Enumerable.Range(Start.Value, Length.Value).Select(i =>
                    {
                        Building?.Invoke(this, new Range(Start.Value, i));
                        var bgra32 = _scene.Render(i, ApplyType.Video);
                        if (LowResolution)
                        {
                            var resized = bgra32.Resize(bgra32.Width / 2, bgra32.Height / 2, Quality.High);
                            bgra32.Dispose();
                            return resized;
                        }
                        else
                        {
                            return bgra32;
                        }
                    }).ToArray();
                }
            }

            public void Dispose()
            {
                if (_bgra16 != null)
                {
                    foreach (var item in _bgra16)
                    {
                        item.Dispose();
                    }

                    _bgra16 = null;
                }

                if (_bgra32 != null)
                {
                    foreach (var item in _bgra32)
                    {
                        item.Dispose();
                    }

                    _bgra32 = null;
                }
            }

            public Image<BGRA32> ReadImage(Frame frame)
            {
                var image = LowColor switch
                {
                    true => _bgra16?[frame - Start]?.Convert<BGRA32>(),
                    false => _bgra32?[frame - Start]?.Clone(),
                };

                if (image == null)
                    return new Image<BGRA32>(_scene.Width, _scene.Height, default(BGRA32));

                if (LowResolution)
                {
                    var resized = image.Resize(_scene.Width, _scene.Height, Quality.Medium);
                    image.Dispose();
                    return resized;
                }

                return image;
            }
        }

        private class StoragePreview : IPreview
        {
            private readonly Scene _scene;
            private readonly long _imageBytes;
            private readonly string _fileName;
            private readonly Stream _stream;

            public StoragePreview(Scene scene, Frame start, Frame length, bool lowColor, bool lowResolution)
            {
                _scene = scene;
                _fileName = Path.Combine(scene.Parent.DirectoryName, ".app", "cache", $"{scene.Name}.prvs");
                _stream = new FileStream(_fileName, FileMode.Create);
                Start = start;
                Length = length;
                LowColor = lowColor;
                LowResolution = lowResolution;

                var scale = LowResolution ? 2 : 1;
                _imageBytes = scene.Width / scale * scene.Height / scale;
                _imageBytes *= LowColor ? 2 : 4;
            }

            public event EventHandler<Range>? Building;

            public Frame Start { get; }

            public Frame Length { get; }

            public bool LowColor { get; }

            public bool LowResolution { get; }

            public void Build()
            {
                if (LowColor)
                {
                    for (var i = Start.Value; i < Length.Value + Start.Value; i++)
                    {
                        Building?.Invoke(this, new Range(Start.Value, i));
                        using var bgra32 = _scene.Render(i, ApplyType.Video);

                        if (LowResolution)
                        {
                            using var resized = bgra32.Resize(bgra32.Width / 2, bgra32.Height / 2, Quality.High);
                            WriteStream(resized.Convert<Bgra4444>());
                        }
                        else
                        {
                            WriteStream(bgra32.Convert<Bgra4444>());
                        }
                    }
                }
                else
                {
                    for (var i = Start.Value; i < Length.Value + Start.Value; i++)
                    {
                        Building?.Invoke(this, new Range(Start.Value, i));
                        var bgra32 = _scene.Render(i, ApplyType.Video);
                        if (LowResolution)
                        {
                            var resized = bgra32.Resize(bgra32.Width / 2, bgra32.Height / 2, Quality.High);
                            bgra32.Dispose();
                            bgra32 = resized;
                        }

                        WriteStream(bgra32);
                    }
                }

                _stream.Flush();
            }

            public void Dispose()
            {
                _stream.Dispose();
                File.Delete(_fileName);
            }

            public Image<BGRA32> ReadImage(Frame frame)
            {
                if (LowColor)
                {
                    if (LowResolution)
                    {
                        using var image = ReadFile<Bgra4444>(frame);
                        using var bgra32 = image.Convert<BGRA32>();
                        return bgra32.Resize(_scene.Width, _scene.Height, Quality.Medium);
                    }
                    else
                    {
                        using var image = ReadFile<Bgra4444>(frame);
                        return image.Convert<BGRA32>();
                    }
                }
                else if (LowResolution)
                {
                    using var image = ReadFile<BGRA32>(frame);
                    return image.Resize(_scene.Width, _scene.Height, Quality.Medium);
                }
                else
                {
                    return ReadFile<BGRA32>(frame);
                }
            }

            private Image<TPixel> ReadFile<TPixel>(Frame frame)
                where TPixel : unmanaged, IPixel<TPixel>
            {
                var offset = (frame.Value - Start.Value) * _imageBytes;
                var scale = LowResolution ? 2 : 1;
                var image = new Image<TPixel>(_scene.Width / scale, _scene.Height / scale);

                _stream.Seek(offset, SeekOrigin.Begin);
                _stream.Read(MemoryMarshal.AsBytes(image.Data));
                return image;
            }

            private void WriteStream<TPixel>(Image<TPixel> image)
                where TPixel : unmanaged, IPixel<TPixel>
            {
                _stream.Write(MemoryMarshal.AsBytes(image.Data));
                image.Dispose();
            }
        }
    }
}