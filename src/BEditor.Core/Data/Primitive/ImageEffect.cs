using System.Runtime.Serialization;

using BEditor.Core.Data.Primitive.Objects;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive
{
    /// <summary>
    /// Represents an effect that can be added to an <see cref="ImageObject"/>.
    /// </summary>
    [DataContract]
    public abstract class ImageEffect : EffectElement
    {
        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public abstract void Render(EffectRenderArgs<Image<BGRA32>> args);

        /// <inheritdoc cref="Render(EffectRenderArgs{Image{BGRA32}})"/>
        public virtual void Render(EffectRenderArgs<ImageInfo> args)
        {
            Render(new EffectRenderArgs<Image<BGRA32>>(args.Frame, args.Value.Source, args.Type));
        }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args) { }
    }
}
