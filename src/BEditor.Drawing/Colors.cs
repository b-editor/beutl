// Colors.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Indicates the web colors.
    /// </summary>
    public static class Colors
    {
        /// <summary>
        /// Gets the color (A: 255 R: 255 G: 255 B: 255).
        /// </summary>
        public static Color White => Color.FromARGB(255, 255, 255, 255);

        /// <summary>
        /// Gets the color (A: 255 R: 0 G: 0 B: 0).
        /// </summary>
        public static Color Black => Color.FromARGB(255, 255, 255, 255);

        /// <summary>
        /// Gets the color (A: 255 R: 255 G: 0 B: 0).
        /// </summary>
        public static Color Red => Color.FromARGB(255, 255, 0, 0);

        /// <summary>
        /// Gets the color (A: 255 R: 0 G: 0 B: 255).
        /// </summary>
        public static Color Blue => Color.FromARGB(255, 0, 0, 255);
    }
}