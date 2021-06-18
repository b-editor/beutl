// Cube.cs
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

using BEditor.Graphics.Platform;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an cube.
    /// </summary>
    public sealed class Cube : Drawable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        public Cube(float width, float height, float depth)
            : base(IPlatform.Current.CreateCube(width, height, depth))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="impl">The cube implementation.</param>
        public Cube(ICubeImpl impl)
            : base(impl)
        {
        }

        /// <summary>
        /// Gets the width of this <see cref="Cube"/>.
        /// </summary>
        public float Width => PlatformImpl.Width;

        /// <summary>
        /// Gets the height of this <see cref="Cube"/>.
        /// </summary>
        public float Height => PlatformImpl.Height;

        /// <summary>
        /// Gets the depth of this <see cref="Cube"/>.
        /// </summary>
        public float Depth => PlatformImpl.Depth;

        /// <summary>
        /// Gets the vertices of this <see cref="Cube"/>.
        /// </summary>
        public ReadOnlyMemory<float> Vertices => PlatformImpl.Vertices;

        /// <summary>
        /// Gets the cube implementation.
        /// </summary>
        public new ICubeImpl PlatformImpl => (ICubeImpl)base.PlatformImpl;
    }
}
