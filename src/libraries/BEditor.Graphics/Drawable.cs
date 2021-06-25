// Drawable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the base class of drawable objects.
    /// </summary>
    public abstract class Drawable : IDisposable
    {
        /// <summary>
        /// Finalizes an instance of the <see cref="Drawable"/> class.
        /// </summary>
        ~Drawable()
        {
            if (!IsDisposed)
            {
                Dispose(false);
            }
        }

        /// <summary>
        /// Gets or sets the color of this <see cref="Drawable"/>.
        /// </summary>
        public Color Color { get; set; } = Colors.White;

        /// <summary>
        /// Gets or sets the material of this <see cref="Drawable"/>.
        /// </summary>
        public Material Material { get; set; } = new(Colors.White, Colors.White, Colors.White, 16);

        /// <summary>
        /// Gets or sets the transform of this <see cref="Drawable"/>.
        /// </summary>
        public Transform Transform { get; set; } = Transform.Default;

        /// <summary>
        /// Gets or sets the rasterizer state.
        /// </summary>
        public RasterizerState RasterizerState { get; set; } = RasterizerState.CullNone;

        /// <summary>
        /// Gets or sets the blend mode of this <see cref="Drawable"/>.
        /// </summary>
        public BlendMode BlendMode { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Dispose(true);

                GC.SuppressFinalize(this);

                IsDisposed = true;
            }
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}