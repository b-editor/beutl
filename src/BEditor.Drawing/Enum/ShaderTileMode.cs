// ShaderTileMode.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Indications on how the shader should handle drawing outside the original bounds.
    /// </summary>
    public enum ShaderTileMode
    {
        /// <summary>
        /// Replicate the edge color.
        /// </summary>
        Clamp,

        /// <summary>
        /// Repeat the shader's image horizontally and vertically.
        /// </summary>
        Repeat,

        /// <summary>
        /// Repeat the shader's image horizontally and vertically, alternating mirror images so that adjacent images always seam.
        /// </summary>
        Mirror,

        /// <summary>
        /// Only draw within the original domain, return transparent-black everywhere else.
        /// </summary>
        Decal,
    }
}