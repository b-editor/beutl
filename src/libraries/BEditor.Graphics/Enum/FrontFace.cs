// FrontFace.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// The winding order used to determine the front face of a primitive.
    /// </summary>
    public enum FrontFace : byte
    {
        /// <summary>
        /// Clockwise winding order.
        /// </summary>
        Clockwise = 0,

        /// <summary>
        /// Counter-clockwise winding order.
        /// </summary>
        CounterClockwise = 1,
    }
}