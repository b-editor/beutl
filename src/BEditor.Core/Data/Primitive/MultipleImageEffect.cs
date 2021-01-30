using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive
{
    /// <summary>
    /// 
    /// </summary>
    [DataContract]
    public abstract class MultipleImageEffect : ImageEffect
    {
        /// <inheritdoc/>
        public abstract IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<Image<BGRA32>> args);
        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {

        }
    }
}
