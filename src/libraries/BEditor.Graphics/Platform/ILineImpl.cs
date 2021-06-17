// ILineImpl.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Defines a line implementation.
    /// </summary>
    public interface ILineImpl : IDrawableImpl
    {
        /// <summary>
        /// Gets the vertices of this <see cref="ILineImpl"/>.
        /// </summary>
        public ReadOnlyMemory<float> Vertices { get; }

        /// <summary>
        /// Gets the start position of this <see cref="ILineImpl"/>.
        /// </summary>
        public Vector3 Start { get; }

        /// <summary>
        /// Gets the end position of this <see cref="ILineImpl"/>.
        /// </summary>
        public Vector3 End { get; }

        /// <summary>
        /// Gets the width of this <see cref="ILineImpl"/>.
        /// </summary>
        public float Width { get; }
    }
}
