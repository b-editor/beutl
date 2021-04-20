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
    /// Represents an <see cref="ImageEffect"/> that monochromatizes an image.
    /// </summary>
    public sealed class SetColor : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<SetColor, ColorProperty> ColorProperty = ColorKey.ColorProperty.WithOwner<SetColor>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="SetColor"/> class.
        /// </summary>
#pragma warning disable CS8618
        public SetColor()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Monoc;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Color;
            }
        }

        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color to be monochromatic.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.SetColor(Color.Value);
        }
    }
}