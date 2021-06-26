// StencilOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// Identifies an action taken on samples that pass or fail the stencil test.
    /// </summary>
    public enum StencilOperation : byte
    {
        /// <summary>
        /// Keep the existing value.
        /// </summary>
        Keep = 0,

        /// <summary>
        /// Sets the value to 0.
        /// </summary>
        Zero = 1,

        /// <summary>
        /// Replaces the existing value with <see cref="DepthStencilState.StencilReference"/>.
        /// </summary>
        Replace = 2,

        /// <summary>
        /// Increments the existing value and clamps it to the maximum representable unsigned value.
        /// </summary>
        IncrementAndClamp = 3,

        /// <summary>
        /// Decrements the existing value and clamps it to 0.
        /// </summary>
        DecrementAndClamp = 4,

        /// <summary>
        /// Bitwise-inverts the existing value.
        /// </summary>
        Invert = 5,

        /// <summary>
        /// Increments the existing value and wraps it to 0 when it exceeds the maximum representable unsigned value.
        /// </summary>
        IncrementAndWrap = 6,

        /// <summary>
        /// Decrements the existing value and wraps it to the maximum representable unsigned value if it would be reduced below 0.
        /// </summary>
        DecrementAndWrap = 7,
    }
}