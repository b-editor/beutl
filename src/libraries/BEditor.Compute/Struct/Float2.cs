// Float2.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.InteropServices;

namespace BEditor.Compute
{
    /// <summary>
    /// A vector of 2 32-bit floating-point values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Float2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Float2"/> struct.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        public Float2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Float2(Float2 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Float2(Float3 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Float2(Float4 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Gets or sets the x.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the y.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the xy.
        /// </summary>
        public Float2 XY
        {
            get => this;
            set => this = value;
        }
    }
}