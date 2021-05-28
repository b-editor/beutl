// FontStyleWeight.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Defines the font weight to be used in <see cref="Font"/>.
    /// </summary>
    public enum FontStyleWeight
    {
        /// <summary>
        /// The font has no thickness at all.
        /// </summary>
        Invisible = 0,

        /// <summary>
        /// A thick font weight of 100.
        /// </summary>
        Thin = 100,

        /// <summary>
        /// A thick font weight of 200.
        /// </summary>
        ExtraLight = 200,

        /// <summary>
        /// A thick font weight of 300.
        /// </summary>
        Light = 300,

        /// <summary>
        /// A typical font weight of 400. This is the default font weight.
        /// </summary>
        Normal = 400,

        /// <summary>
        /// A thick font weight of 500.
        /// </summary>
        Medium = 500,

        /// <summary>
        /// A thick font weight of 600.
        /// </summary>
        SemiBold = 600,

        /// <summary>
        /// A thick font weight of 700. This is the default for a bold font.
        /// </summary>
        Bold = 700,

        /// <summary>
        /// A thick font weight of 800.
        /// </summary>
        ExtraBold = 800,

        /// <summary>
        /// A thick font weight of 900.
        /// </summary>
        Black = 900,

        /// <summary>
        /// A thick font weight of 1000.
        /// </summary>
        ExtraBlack = 1000,
    }
}