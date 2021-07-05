// Shadow.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
    /// Represents the <see cref="ImageEffect"/> that adds a shadow to an image.
    /// </summary>
    public sealed class Shadow : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectProperty<Shadow, EaseProperty> XProperty = EditingProperty.RegisterDirect<EaseProperty, Shadow>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.X, 10)).Serialize());

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectProperty<Shadow, EaseProperty> YProperty = EditingProperty.RegisterDirect<EaseProperty, Shadow>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Y, 10)).Serialize());

        /// <summary>
        /// Defines the <see cref="Blur"/> property.
        /// </summary>
        public static readonly DirectProperty<Shadow, EaseProperty> BlurProperty = EditingProperty.RegisterDirect<EaseProperty, Shadow>(
            nameof(Blur),
            owner => owner.Blur,
            (owner, obj) => owner.Blur = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Blur, 10, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectProperty<Shadow, EaseProperty> OpacityProperty = EditingProperty.RegisterDirect<EaseProperty, Shadow>(
            nameof(Opacity),
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Opacity, 75, 100, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Shadow, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, Shadow>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.Black)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Shadow"/> class.
        /// </summary>
        public Shadow()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.DropShadow;

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
            var frame = args.Frame;
            var img = args.Value.Shadow(X[frame], Y[frame], Blur[frame], Opacity[frame] / 100, Color.Value);

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