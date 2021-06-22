// BlendMode.cs
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

namespace BEditor.Graphics
{
    /// <summary>
    /// Defines the mode of the blend.
    /// </summary>
    public enum BlendMode
    {
        /// <summary>
        /// The default.
        /// </summary>
        AlphaBlend = 0,

        /// <summary>
        /// The additive.
        /// </summary>
        Additive = 1,

        /// <summary>
        /// The subtract.
        /// </summary>
        Subtract = 2,

        /// <summary>
        /// The multiplication.
        /// </summary>
        Multiplication = 3,
    }
}