using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that adds an inner shadow to an image.
    /// </summary>
    public sealed class InnerShadow : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<InnerShadow, EaseProperty> XProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, InnerShadow>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            new EasePropertyMetadata(Strings.X, 10));

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<InnerShadow, EaseProperty> YProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, InnerShadow>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            new EasePropertyMetadata(Strings.Y, 10));

        /// <summary>
        /// Defines the <see cref="Blur"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<InnerShadow, EaseProperty> BlurProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, InnerShadow>(
            nameof(Blur),
            owner => owner.Blur,
            (owner, obj) => owner.Blur = obj,
            new EasePropertyMetadata(Strings.Blur, 10, Min: 0));

        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<InnerShadow, EaseProperty> OpacityProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, InnerShadow>(
            nameof(Opacity),
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            new EasePropertyMetadata(Strings.Opacity, 75, 100, 0));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<InnerShadow, ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, InnerShadow>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            new ColorPropertyMetadata(Strings.Color, Drawing.Color.Dark));

        /// <inheritdoc/>
        public override string Name => Strings.InnerShadow;

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the blur sigma.
        /// </summary>
        [AllowNull]
        public EaseProperty Blur { get; private set; }

        /// <summary>
        /// Gets the opacity.
        /// </summary>
        [AllowNull]
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Gets the shadow color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var x = (int)X[args.Frame];
            var y = (int)Y[args.Frame];
            var blur = Blur[args.Frame];
            var opacity = Opacity[args.Frame];

            var img = args.Value.InnerShadow(x, y, blur, opacity, Color.Value, Parent.Parent.DrawingContext);

            args.Value.Dispose();
            args.Value = img;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Blur;
            yield return Opacity;
            yield return Color;
        }
    }
}