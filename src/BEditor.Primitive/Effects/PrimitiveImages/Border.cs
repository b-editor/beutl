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
    /// Represents an <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class Border : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Border, EaseProperty> SizeProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Border>(
            nameof(Size), owner => owner.Size, (owner, obj) => owner.Size = obj, new EasePropertyMetadata(Strings.Size, 10, float.NaN, 1));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Border, ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, Border>(
            nameof(Color), owner => owner.Color, (owner, obj) => owner.Color = obj, new ColorPropertyMetadata(Strings.Color, Drawing.Color.Light));

        /// <summary>
        /// Initializes a new instance of the <see cref="Border"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Border()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Border;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };

        /// <summary>
        /// Gets the size of the edge.
        /// </summary>
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the edge color.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Border((int)Size!.GetValue(args.Frame), Color!.Value);
            args.Value.Dispose();

            args.Value = img;
        }
    }
}