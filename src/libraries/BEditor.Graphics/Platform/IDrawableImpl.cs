// IDrawableImpl.cs
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

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Defines a drawable object implementation.
    /// </summary>
    public interface IDrawableImpl : IDisposable
    {
        /// <summary>
        /// Gets or sets the color of this <see cref="IDrawableImpl"/>.
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// Gets or sets the blend mode of this <see cref="IDrawableImpl"/>.
        /// </summary>
        public BlendMode BlendMode { get; set; }

        /// <summary>
        /// Gets or sets the material of this <see cref="IDrawableImpl"/>.
        /// </summary>
        public Material Material { get; set; }

        /// <summary>
        /// Gets or sets the transform of this <see cref="IDrawableImpl"/>.
        /// </summary>
        public Transform Transform { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; }
    }
}
