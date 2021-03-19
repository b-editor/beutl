using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor
{
    /// <summary>
    /// Represents the rendering result.
    /// </summary>
    public readonly ref struct RenderingResult
    {
        /// <summary>
        /// 
        /// </summary>
        public Image<BGRA32> Image { get; init; }
    }
}
