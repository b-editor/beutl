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
    /// Represents an <see cref="ImageEffect"/> that adds a shadow to an image.
    /// </summary>
    public sealed class Shadow : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shadow, EaseProperty> XProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Shadow>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            new EasePropertyMetadata(Strings.X, 10));

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shadow, EaseProperty> YProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Shadow>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            new EasePropertyMetadata(Strings.Y, 10));

        /// <summary>
        /// Defines the <see cref="Blur"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shadow, EaseProperty> BlurProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Shadow>(
            nameof(Blur),
            owner => owner.Blur,
            (owner, obj) => owner.Blur = obj,
            new EasePropertyMetadata(Strings.Blur, 10, Min: 0));

        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shadow, EaseProperty> OpacityProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Shadow>(
            nameof(Opacity),
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            new EasePropertyMetadata(Strings.Opacity, 75, 100, 0));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shadow, ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, Shadow>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            new ColorPropertyMetadata(Strings.Color, Drawing.Color.Dark));

        /// <summary>
        /// Initializes a new instance of the <see cref="Shadow"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Shadow()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.DropShadow;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Blur;
                yield return Opacity;
                yield return Color;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the blur sigma.
        /// </summary>
        public EaseProperty Blur { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the transparency.
        /// </summary>
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the shadow color.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var frame = args.Frame;
            var img = args.Value.Shadow(X[frame], Y[frame], Blur[frame], Opacity[frame] / 100, Color.Value);

            args.Value.Dispose();

            args.Value = img;
        }
    }
}