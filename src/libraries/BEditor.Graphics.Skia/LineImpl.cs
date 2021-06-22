// LineImpl.cs
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

using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Skia
{
    /// <summary>
    /// The line implementation.
    /// </summary>
    public sealed class LineImpl : DrawableImpl, ILineImpl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LineImpl"/> class.
        /// </summary>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="width">The width of the line.</param>
        public LineImpl(Vector3 start, Vector3 end, float width)
        {
            Start = start;
            End = end;
            Width = width;
        }

        /// <inheritdoc/>
        public float[] Vertices => throw new NotImplementedException();

        /// <inheritdoc/>
        public Vector3 Start { get; }

        /// <inheritdoc/>
        public Vector3 End { get; }

        /// <inheritdoc/>
        public float Width { get; }
    }
}
