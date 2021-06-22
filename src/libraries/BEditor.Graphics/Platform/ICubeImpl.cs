// ICubeImpl.cs
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
    /// Defines a cube implementation.
    /// </summary>
    public interface ICubeImpl : IDrawableImpl
    {
        /// <summary>
        /// Gets the width of this <see cref="ICubeImpl"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="ICubeImpl"/>.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Gets the depth of this <see cref="ICubeImpl"/>.
        /// </summary>
        public float Depth { get; }

        /// <summary>
        /// Gets the vertices of this <see cref="ICubeImpl"/>.
        /// </summary>
        public float[] Vertices { get; }
    }
}
