// ColorVector.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing
{
    /// <summary>
    /// The color vector.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ColorVector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorVector"/> struct.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <param name="a">The alpha component.</param>
        public ColorVector(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
            W = 1;
        }

        /// <summary>
        /// Gets or sets the red component value of this <see cref="ColorVector"/>.
        /// </summary>
        public float R { get; set; }

        /// <summary>
        /// Gets or sets the green component value of this <see cref="ColorVector"/>.
        /// </summary>
        public float G { get; set; }

        /// <summary>
        /// Gets or sets the blue component value of this <see cref="ColorVector"/>.
        /// </summary>
        public float B { get; set; }

        /// <summary>
        /// Gets or sets the alpha component value of this <see cref="ColorVector"/>.
        /// </summary>
        public float A { get; set; }

        /// <summary>
        /// Gets or sets the w component value of this <see cref="ColorVector"/>.
        /// </summary>
        public float W { get; set; }
    }
}