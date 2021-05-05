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
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blur, EaseProperty> TopProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Blur>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            new EasePropertyMetadata(Strings.Size, 25, float.NaN, 0));

        /// <summary>
        /// Initializes a new instance of the <see cref="Blur"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Blur()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Blur;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Size;
            }
        }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the blur sigma.
        /// </summary>
        public EaseProperty Size { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var size = (int)Size.GetValue(args.Frame);
            if (size is 0) return;

            args.Value.Blur(size);
        }
    }
}