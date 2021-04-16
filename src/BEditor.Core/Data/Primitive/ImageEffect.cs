using System.Collections.Generic;
using System.Linq;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Data.Primitive
{
    /// <summary>
    /// Represents an effect that can be added to an <see cref="ImageObject"/>.
    /// </summary>
    public abstract class ImageEffect : EffectElement
    {
        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public abstract void Render(EffectRenderArgs<Image<BGRA32>> args);

        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public virtual void Render(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            args.Value = args.Value.Select(img =>
            {
                var a = new EffectRenderArgs<Image<BGRA32>>(args.Frame, img.Source, args.Type);
                Render(a);
                img.Source = a.Value;

                return img;
            });
        }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
        }
    }
}