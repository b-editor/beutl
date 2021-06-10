// FlipMode.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Drawing
{
    /// <summary>
    /// The flip mode used by a Image.Flip.
    /// </summary>
    [Flags]
    public enum FlipMode
    {
        /// <summary>
        /// Specifies to flip horizontally.
        /// </summary>
        X = 0,

        /// <summary>
        /// Specifies to flip vertically.
        /// </summary>
        Y = 1,
    }
}