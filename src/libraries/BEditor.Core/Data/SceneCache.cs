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
        private Image<BGRA32>[]? _images;

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
        public void Create(Frame start, Frame length)
        {
            Clear();
            _images = Enumerable.Range(start.Value, length.Value).Select(i =>
            {
                Building?.Invoke(this, new Range(start.Value, i));
                return _scene.Render(i, ApplyType.Video);
            }).ToArray();

            Start = start;
            Length = length;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void Clear()
        {
            if (_images != null)
            {
                foreach (var item in _images)
                {
                    item.Dispose();
                }

                _images = null;
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
            return _images?[frame - Start]?.Clone() ?? new Image<BGRA32>(Width, Height, default(BGRA32));
        }
    }
}
