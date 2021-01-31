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
        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public abstract IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<Image<BGRA32>> args);

        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public virtual IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<ImageInfo> args)
        {
            return MultipleRender(new EffectRenderArgs<Image<BGRA32>>(args.Frame, args.Value.Source, args.Type));
        }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {

        }
    }
}
