// Double4.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.InteropServices;

namespace BEditor.Compute
{
    /// <summary>
    /// A vector of 4 64-bit floating-point values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Double4
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Double4"/> struct.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <param name="z">The z.</param>
        /// <param name="w">The w.</param>
        public Double4(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <param name="z">The z.</param>
        /// <param name="w">The w.</param>
        public Double4(Double2 vector, double z, double w)
        {
            X = vector.X;
            Y = vector.Y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <param name="w">The w.</param>
        public Double4(Double3 vector, double w)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Double4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Double4(Double4 vector)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
            W = vector.W;
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
        /// Gets or sets the z.
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// Gets or sets the w.
        /// </summary>
        public double W { get; set; }

        /// <summary>
        /// Gets or sets the xy.
        /// </summary>
        public Double2 XY
        {
            get => new(X, Y);
            set
            {
                X = value.X;
                Y = value.Y;
            }
        }

        /// <summary>
        /// Gets or sets the xyz.
        /// </summary>
        public Double3 XYZ
        {
            get => new(X, Y, Z);
            set
            {
                X = value.X;
                Y = value.Y;
                Z = value.Z;
            }
        }

        /// <summary>
        /// Gets or sets the xyzw.
        /// </summary>
        public Double4 XYZW
        {
            get => this;
            set => this = value;
        }
    }
}