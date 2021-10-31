// FlipMode.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Drawing
{
#pragma warning disable RCS1135
    /// <summary>
    /// The flip mode used by a Image.Flip.
    /// </summary>
    [Flags]
    public enum FlipMode
    {
        /// <summary>
        /// Specifies to flip horizontally.
        /// </summary>
        X = 1,

        /// <summary>
        /// Specifies to flip vertically.
        /// </summary>
        Y = 2,
    }
#pragma warning restore RCS1135
}