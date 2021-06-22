// Line.cs
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
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        public Line(Vector3 start, Vector3 end, float width)
            : base(IPlatform.Current.CreateLine(start, end, width))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Line"/> class.
        /// </summary>
        /// <param name="impl">The line implementation.</param>
        public Line(ILineImpl impl)
            : base(impl)
        {
        }

        /// <summary>
        /// Gets the vertices of this <see cref="Line"/>.
        /// </summary>
        public float[] Vertices => PlatformImpl.Vertices;

        /// <summary>
        /// Gets the start position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 Start => PlatformImpl.Start;

        /// <summary>
        /// Gets the end position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 End => PlatformImpl.End;

        /// <summary>
        /// Gets the width of this <see cref="Line"/>.
        /// </summary>
        public float Width => PlatformImpl.Width;

        /// <summary>
        /// Gets the line implementation.
        /// </summary>
        public new ILineImpl PlatformImpl => (ILineImpl)base.PlatformImpl;
    }
}
