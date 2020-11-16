using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media;

namespace BEditor.ObjectModel
{
    /// <summary>
    /// レンダリング結果を表します
    /// </summary>
    public readonly ref struct RenderingResult
    {
        /// <summary>
        /// 
        /// </summary>
        public Image Image { get; init; }
    }
}
