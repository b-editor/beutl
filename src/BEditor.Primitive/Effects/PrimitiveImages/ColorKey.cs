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
    /// Represents a ColorKey effect.
    /// </summary>
    public sealed class ColorKey : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ColorKey, ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, ColorKey>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            new ColorPropertyMetadata(Strings.Color, Drawing.Color.Light));

        /// <summary>
        /// Defines the <see cref="ThresholdValue"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ColorKey, EaseProperty> ThresholdValueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, ColorKey>(
            nameof(ThresholdValue),
            owner => owner.ThresholdValue,
            (owner, obj) => owner.ThresholdValue = obj,
            new EasePropertyMetadata(Strings.ThresholdValue, 60));

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKey"/> class.
        /// </summary>
#pragma warning disable CS8618
        public ColorKey()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ColorKey;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Color;
                yield return ThresholdValue;
            }
        }

        /// <summary>
        /// Gets the <see cref="ColorProperty"/> representing the key color.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the threshold.
        /// </summary>
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.ColorKey(Color.Value, (int)ThresholdValue[args.Frame]);
        }
    }
}