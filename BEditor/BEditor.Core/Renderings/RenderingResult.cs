using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Renderings
{
    /// <summary>
    /// レンダリング結果を表します
    /// </summary>
    public readonly ref struct RenderingResult
    {
        /// <summary>
        /// 
        /// </summary>
        public Image<BGRA32> Image { get; init; }
    }
}
