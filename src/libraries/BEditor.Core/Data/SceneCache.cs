// SceneCache.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private Image<Bgra4444>[]? _bgra16;
        private Image<BGRA32>[]? _bgra32;

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
        public Frame Start { get; private set; }

        /// <summary>
        /// Gets the length of the cache.
        /// </summary>
        public Frame Length { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the color reproduction is reduced.
        /// </summary>
        public bool LowColor { get; private set; }

        /// <summary>
        /// Gets a value indicating whether or not to lower the resolution.
        /// </summary>
        public bool LowResolution { get; private set; }

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
            if (lowColor)
            {
                _bgra16 = Enumerable.Range(start.Value, length.Value).Select(i =>
                {
                    Building?.Invoke(this, new Range(start.Value, i));
                    using var bgra32 = _scene.Render(i, ApplyType.Video);

                    if (lowResolution)
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
                _bgra32 = Enumerable.Range(start.Value, length.Value).Select(i =>
                {
                    Building?.Invoke(this, new Range(start.Value, i));
                    var bgra32 = _scene.Render(i, ApplyType.Video);
                    if (lowResolution)
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

            LowColor = lowColor;
            LowResolution = lowResolution;
            Start = start;
            Length = length;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void Clear()
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

            Start = 0;
            Length = 0;

            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Read the image from the cache.
        /// </summary>
        /// <param name="frame">The frame to read.</param>
        /// <returns>Returns the image that was read.</returns>
        public unsafe Image<BGRA32> ReadImage(Frame frame)
        {
            var image = LowColor switch
            {
                true => _bgra16?[frame - Start]?.Convert<BGRA32>(),
                false => _bgra32?[frame - Start]?.Clone(),
            };

            if (image == null)
                return new Image<BGRA32>(Width, Height, default(BGRA32));

            if (LowResolution)
            {
                var resized = image.Resize(Width, Height, Quality.Medium);
                image.Dispose();
                return resized;
            }

            return image;
        }
    }
}
