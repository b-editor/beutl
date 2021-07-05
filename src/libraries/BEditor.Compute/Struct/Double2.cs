// Double2.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.InteropServices;

namespace BEditor.Compute
{
    /// <summary>
    /// A vector of 2 64-bit floating-point values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Double2
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Double2"/> struct.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        public Double2(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Double2(Double2 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Double2(Double3 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double2"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Double2(Double4 vector)
        {
            X = vector.X;
            Y = vector.Y;
        }

        /// <summary>
        /// Gets or sets the x.
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Gets or sets the y.
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Gets or sets the xy.
        /// </summary>
        public Double2 XY
        {
            get => this;
            set => this = value;
        }
    }
}