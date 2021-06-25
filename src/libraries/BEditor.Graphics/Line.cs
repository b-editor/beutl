// Line.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Numerics;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an line.
    /// </summary>
    public sealed class Line : Drawable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Line"/> class.
        /// </summary>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="width">The width of the line.</param>
        public Line(Vector3 start, Vector3 end, float width)
        {
            Start = start;
            End = end;
            Width = width;
        }

        /// <summary>
        /// Gets the start position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 Start { get; }

        /// <summary>
        /// Gets the end position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 End { get; }

        /// <summary>
        /// Gets the width of this <see cref="Line"/>.
        /// </summary>
        public float Width { get; }
    }
}