// Border.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class Border : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, EaseProperty> CenterXProperty = Coordinate.XProperty.WithOwner<Border>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, EaseProperty> CenterYProperty = Coordinate.YProperty.WithOwner<Border>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, EaseProperty> SizeProperty = EditingProperty.RegisterDirect<EaseProperty, Border>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Size, 10, float.NaN, 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, EaseProperty> OpacityProperty = Shadow.OpacityProperty.WithOwner<Border>(
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Opacity, 100, 100, 0)));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, Border>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.White)).Serialize());

        /// <summary>
        /// Defines the <see cref="Mask"/> property.
        /// </summary>
        public static readonly DirectProperty<Border, SelectorProperty> MaskProperty = EditingProperty.RegisterDirect<SelectorProperty, Border>(
            nameof(Mask),
            owner => owner.Mask,
            (owner, obj) => owner.Mask = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.MaskType, new string[]
            {
                Strings.Unspecified,
                Strings.InvertMask,
                Strings.DoNotInvertMask,
            })).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Border"/> class.
        /// </summary>
        public Border()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Border;

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
        /// Gets the size of the edge.
        /// </summary>
        [AllowNull]
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the opacity.
        /// </summary>
        [AllowNull]
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Gets the border color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the mask type.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mask { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Border((int)Size.GetValue(args.Frame), Color.Value);
            args.Value.Dispose();

            args.Value = img;
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.SelectMany(i => Selector(i, args));
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Size;
            yield return Opacity;
            yield return Color;
            yield return Mask;
        }

        private IEnumerable<Texture> Selector(Texture texture, EffectApplyArgs args)
        {
            var frame = args.Frame;
            var color = Color.Value;
            var size = (int)Size.GetValue(frame);
            color.A = (byte)(color.A * (Opacity[frame] / 100));

            using var source = texture.ToImage();
            using var border = source.Border(size, color);

            if (Mask.Value is 1 or 2)
            {
                border.Mask(source, default, 0, Mask.Value == 2, args.Contexts.Drawing);
            }

            var borderTexture = Texture.FromImage(border);
            borderTexture.Synchronize(texture);

            // Transformを変更
            var transform = borderTexture.Transform;
            transform.Position += new Vector3(X[frame], Y[frame], 0);
            borderTexture.Transform = transform;

            yield return borderTexture;
            yield return texture;
        }
    }
}