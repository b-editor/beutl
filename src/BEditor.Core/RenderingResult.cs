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
