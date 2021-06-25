// Ball.cs
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
    /// Represents an ball.
    /// </summary>
    public sealed class Ball : Drawable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Ball"/> class.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        public Ball(float radiusX, float radiusY, float radiusZ)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            RadiusZ = radiusZ;
        }

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the X-axis direction.
        /// </summary>
        public float RadiusX { get; }

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the Y-axis direction.
        /// </summary>
        public float RadiusY { get; }

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the Z-axis direction.
        /// </summary>
        public float RadiusZ { get; }
    }
}