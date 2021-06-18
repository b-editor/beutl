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
        /// Initializes a new instance of the <see cref="Drawable"/> class.
        /// </summary>
        /// <param name="impl">The drawable object implementation.</param>
        protected Drawable(IDrawableImpl impl)
        {
            PlatformImpl = impl;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Drawable"/> class.
        /// </summary>
        ~Drawable()
        {
            if (!IsDisposed)
            {
                Dispose();
            }
        }

        /// <summary>
        /// Gets or sets the color of this <see cref="Drawable"/>.
        /// </summary>
        public Color Color
        {
            get => PlatformImpl.Color;
            set => PlatformImpl.Color = value;
        }

        /// <summary>
        /// Gets or sets the material of this <see cref="Drawable"/>.
        /// </summary>
        public Material Material
        {
            get => PlatformImpl.Material;
            set => PlatformImpl.Material = value;
        }

        /// <summary>
        /// Gets or sets the transform of this <see cref="Drawable"/>.
        /// </summary>
        public Transform Transform
        {
            get => PlatformImpl.Transform;
            set => PlatformImpl.Transform = value;
        }

        /// <summary>
        /// Gets or sets the blend mode of this <see cref="Drawable"/>.
        /// </summary>
        public BlendMode BlendMode
        {
            get => PlatformImpl.BlendMode;
            set => PlatformImpl.BlendMode = value;
        }

        /// <summary>
        /// Gets the drawable object implementation.
        /// </summary>
        public IDrawableImpl PlatformImpl { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed => PlatformImpl.IsDisposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                PlatformImpl.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
