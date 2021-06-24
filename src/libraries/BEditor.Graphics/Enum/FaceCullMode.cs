// FaceCullMode.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// Indicates which face will be culled.
    /// </summary>
    public enum FaceCullMode : byte
    {
        /// <summary>
        /// The back face.
        /// </summary>
        Back = 0,

        /// <summary>
        /// The front face.
        /// </summary>
        Front = 1,

        /// <summary>
        /// No face culling.
        /// </summary>
        None = 2,
    }
}