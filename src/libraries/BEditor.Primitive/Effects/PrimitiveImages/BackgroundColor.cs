// BackgroundColor.cs
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
using BEditor.LangResources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Effects for painting the background color.
    /// </summary>
    public sealed class BackgroundColor : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<BackgroundColor, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, BackgroundColor>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.BackgroundColor, Colors.DodgerBlue)).Serialize());

        /// <summary>
        /// Defines the <see cref="PaddingLeft"/> property.
        /// </summary>
        public static readonly DirectProperty<BackgroundColor, EaseProperty> PaddingLeftProperty = EditingProperty.RegisterDirect<EaseProperty, BackgroundColor>(
            nameof(PaddingLeft),
            owner => owner.PaddingLeft,
            (owner, obj) => owner.PaddingLeft = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.PaddingLeft, 8, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="PaddingTop"/> property.
        /// </summary>
        public static readonly DirectProperty<BackgroundColor, EaseProperty> PaddingTopProperty = EditingProperty.RegisterDirect<EaseProperty, BackgroundColor>(
            nameof(PaddingTop),
            owner => owner.PaddingTop,
            (owner, obj) => owner.PaddingTop = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.PaddingTop, 8, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="PaddingRight"/> property.
        /// </summary>
        public static readonly DirectProperty<BackgroundColor, EaseProperty> PaddingRightProperty = EditingProperty.RegisterDirect<EaseProperty, BackgroundColor>(
            nameof(PaddingRight),
            owner => owner.PaddingRight,
            (owner, obj) => owner.PaddingRight = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.PaddingRight, 8, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="PaddingBottom"/> property.
        /// </summary>
        public static readonly DirectProperty<BackgroundColor, EaseProperty> PaddingBottomProperty = EditingProperty.RegisterDirect<EaseProperty, BackgroundColor>(
            nameof(PaddingBottom),
            owner => owner.PaddingBottom,
            (owner, obj) => owner.PaddingBottom = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.PaddingBottom, 8, min: 0)).Serialize());

        /// <summary>
        /// Gets the background color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the left padding.
        /// </summary>
        [AllowNull]
        public EaseProperty PaddingLeft { get; private set; }

        /// <summary>
        /// Gets the top padding.
        /// </summary>
        [AllowNull]
        public EaseProperty PaddingTop { get; private set; }

        /// <summary>
        /// Gets the left padding.
        /// </summary>
        [AllowNull]
        public EaseProperty PaddingRight { get; private set; }

        /// <summary>
        /// Gets the bottom padding.
        /// </summary>
        [AllowNull]
        public EaseProperty PaddingBottom { get; private set; }

        /// <inheritdoc/>
        public override string Name => Strings.BackgroundColor;

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var image = args.Value;
            var f = args.Frame;
            var left = PaddingLeft[f];
            var top = PaddingTop[f];
            var right = PaddingRight[f];
            var bottom = PaddingBottom[f];
            var background = new Image<BGRA32>((int)(image.Width + left + right), (int)(image.Height + top + bottom), Color.Value);
            background.DrawImage(new Point((int)left, (int)top), image, args.Contexts.Drawing);

            image.Dispose();
            args.Value = background;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Color;
            yield return PaddingLeft;
            yield return PaddingTop;
            yield return PaddingRight;
            yield return PaddingBottom;
        }
    }
}
