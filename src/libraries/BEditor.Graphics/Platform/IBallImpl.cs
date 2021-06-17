// IBallImpl.cs
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

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Defines a ball implementation.
    /// </summary>
    public interface IBallImpl : IDrawableImpl
    {
        /// <summary>
        /// Gets the radius of this <see cref="IBallImpl"/> in the X-axis direction.
        /// </summary>
        public float RadiusX { get; }

        /// <summary>
        /// Gets the radius of this <see cref="IBallImpl"/> in the Y-axis direction.
        /// </summary>
        public float RadiusY { get; }

        /// <summary>
        /// Gets the radius of this <see cref="IBallImpl"/> in the Z-axis direction.
        /// </summary>
        public float RadiusZ { get; }

        /// <summary>
        /// Gets the vertices of this <see cref="IBallImpl"/>.
        /// </summary>
        public ReadOnlyMemory<float> Vertices { get; }
    }
}
