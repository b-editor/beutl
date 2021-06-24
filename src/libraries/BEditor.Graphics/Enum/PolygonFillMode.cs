// PolygonFillMode.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// Indicates how the rasterizer will fill polygons.
    /// </summary>
    public enum PolygonFillMode : byte
    {
        /// <summary>
        /// Polygons are filled completely.
        /// </summary>
        Solid = 0,

        /// <summary>
        /// Polygons are outlined in a "wireframe" style.
        /// </summary>
        Wireframe = 1,
    }
}