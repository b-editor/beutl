using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that blurs the image.
    /// </summary>
    public sealed class Blur : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Size"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SizeMetadata = new(Strings.Size, 25, float.NaN, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="Blur"/> class.
        /// </summary>
        public Blur()
        {
            Size = new(SizeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.Blur;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size
        };
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the blur sigma.
        /// </summary>
        [DataMember]
        public EaseProperty Size { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var size = (int)Size.GetValue(args.Frame);
            if (size is 0) return;

            args.Value.Blur(size);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Size.Load(SizeMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Size.Unload();
        }
    }
}
