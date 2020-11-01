using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Core.Media {
    public enum ImageReadMode {
        /// <summary>
        /// IMREAD_UNCHANGED
        /// </summary>
        UnChanged = -1,
        /// <summary>
        /// IMREAD_GRAYSCALE
        /// </summary>
        GrayScale = 0,
        /// <summary>
        /// IMREAD_COLOR
        /// </summary>
        Color = 1,
        /// <summary>
        /// IMREAD_ANYDEPTH
        /// </summary>
        AnyDepth = 2,
        /// <summary>
        /// IMREAD_ANYCOLOR
        /// </summary>
        AnyColor = 4,
        /// <summary>
        /// IMREAD_LOAD_GDAL
        /// </summary>
        LoadGdal = 8,
        /// <summary>
        /// IMREAD_REDUCED_GRAYSCALE_2
        /// </summary>
        ReducedGrayScale2 = 16,
        /// <summary>
        /// IMREAD_REDUCED_COLOR_2
        /// </summary>
        ReducedColor2 = 17,
        /// <summary>
        /// IMREAD_REDUCED_GRAYSCALE_4
        /// </summary>
        ReducedGrayScale4 = 32,
        /// <summary>
        /// IMREAD_REDUCED_COLOR_4
        /// </summary>
        ReducedColor4 = 33,
        /// <summary>
        /// IMREAD_REDUCED_GRAYSCALE_8
        /// </summary>
        ReducedGrayScale8 = 64,
        /// <summary>
        /// IMREAD_REDUCED_COLOR_8
        /// </summary>
        ReducedColor8 = 65,
        /// <summary>
        /// IMREAD_IGNORE_ORIENTATION
        /// </summary>
        IgnoreOrientation = 128
    }
}
