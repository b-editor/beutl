// Float4.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.InteropServices;

namespace BEditor.Compute
{
    /// <summary>
    /// A vector of 4 32-bit floating-point values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Float4
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Float4"/> struct.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <param name="z">The z.</param>
        /// <param name="w">The w.</param>
        public Float4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <param name="z">The z.</param>
        /// <param name="w">The w.</param>
        public Float4(Float2 vector, float z, float w)
        {
            X = vector.X;
            Y = vector.Y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        /// <param name="w">The w.</param>
        public Float4(Float3 vector, float w)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
            W = w;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Float4"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Float4(Float4 vector)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
            W = vector.W;
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
        /// Gets or sets the z.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Gets or sets the w.
        /// </summary>
        public float W { get; set; }

        /// <summary>
        /// Gets or sets the xy.
        /// </summary>
        public Float2 XY
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
        public Float3 XYZ
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
        public Float4 XYZW
        {
            get => this;
            set => this = value;
        }
    }
}