// Brush.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the brush.
    /// </summary>
    public class Brush
    {
        /// <summary>
        /// Gets or sets the color.
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether anti-aliasing is enabled.
        /// </summary>
        public bool IsAntialias { get; set; }

        /// <summary>
        /// Gets or sets the style of the brush.
        /// </summary>
        public BrushStyle Style { get; set; }

        /// <summary>
        /// Gets or sets the width of the stroke.
        /// </summary>
        public int StrokeWidth { get; set; }
    }
}