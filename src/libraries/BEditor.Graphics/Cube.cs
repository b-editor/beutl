// Cube.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        {
            Width = width;
            Height = height;
            Depth = depth;
        }

        /// <summary>
        /// Gets the width of this <see cref="Cube"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="Cube"/>.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Gets the depth of this <see cref="Cube"/>.
        /// </summary>
        public float Depth { get; }
    }
}