// ImageInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Graphics
{
    /// <summary>
    /// A pair of an image and a transformation matrix.
    /// </summary>
    public class ImageInfo : IDisposable
    {
        private readonly Func<ImageInfo, Transform> _getTransform;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageInfo"/> class.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <param name="transform">Gets the tramsform.</param>
        public ImageInfo(Image<BGRA32> image, Func<ImageInfo, Transform> transform)
        {
            Source = image;
            _getTransform = transform;
        }

        /// <summary>
        /// Gets or sets the image.
        /// </summary>
        public Image<BGRA32> Source { get; set; }

        /// <summary>
        /// Gets the <see cref="Graphics.Transform"/> structure.
        /// </summary>
        public Transform Transform => _getTransform(this);

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            Source.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}