// EncodedImageFormat.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// The various formats used by a Image.Encode.
    /// </summary>
    public enum EncodedImageFormat
    {
        /// <summary>
        /// The BMP image format.
        /// </summary>
        Bmp = 0,

        /// <summary>
        /// The GIF image format.
        /// </summary>
        Gif = 1,

        /// <summary>
        /// The ICO image format.
        /// </summary>
        Ico = 2,

        /// <summary>
        /// The JPEG image format.
        /// </summary>
        Jpeg = 3,

        /// <summary>
        /// The PNG image format.
        /// </summary>
        Png = 4,

        /// <summary>
        /// The WBMP image format.
        /// </summary>
        Wbmp = 5,

        /// <summary>
        /// The WEBP image format.
        /// </summary>
        Webp = 6,

        /// <summary>
        /// The PKM image format.
        /// </summary>
        Pkm = 7,

        /// <summary>
        /// The KTX image format.
        /// </summary>
        Ktx = 8,

        /// <summary>
        /// The ASTC image format.
        /// </summary>
        Astc = 9,

        /// <summary>
        /// The Adobe DNG image format.
        /// </summary>
        Dng = 10,

        /// <summary>
        /// The HEIF or High Efficiency Image File format.
        /// </summary>
        Heif = 11,
    }
}