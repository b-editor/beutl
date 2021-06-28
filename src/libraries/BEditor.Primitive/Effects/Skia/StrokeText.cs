// StrokeText.cs
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
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class StrokeText : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectProperty<StrokeText, EaseProperty> CenterXProperty = EditingProperty.RegisterDirect<EaseProperty, StrokeText>(
            nameof(CenterX),
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CenterX, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectProperty<StrokeText, EaseProperty> CenterYProperty = EditingProperty.RegisterDirect<EaseProperty, StrokeText>(
            nameof(CenterY),
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CenterY, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="LineSpacing"/> property.
        /// </summary>
        public static readonly DirectProperty<StrokeText, EaseProperty> LineSpacingProperty = Text.LineSpacingProperty.WithOwner<StrokeText>(
            owner => owner.LineSpacing,
            (owner, obj) => owner.LineSpacing = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.LineSpacing, 0)));

        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectProperty<StrokeText, EaseProperty> SizeProperty = Border.SizeProperty.WithOwner<StrokeText>(
            owner => owner.Size,
            (owner, obj) => owner.Size = obj);

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<StrokeText, ColorProperty> ColorProperty = Border.ColorProperty.WithOwner<StrokeText>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeText"/> class.
        /// </summary>
        public StrokeText()
        {
        }

        /// <inheritdoc/>
        public override string Name => $"{Strings.Border} ({Strings.Text})";

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the line spacing of the string to be drawn.
        /// </summary>
        [AllowNull]
        public EaseProperty LineSpacing { get; private set; }

        /// <summary>
        /// Gets the size of the edge.
        /// </summary>
        [AllowNull]
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the edge color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is Text textObj)
            {
                var stroke = Image.StrokeText(
                    textObj.Document.Value,
                    textObj.Font.Value,
                    textObj.Size[args.Frame],
                    Size[args.Frame],
                    Color.Value,
                    (HorizontalAlign)textObj.HorizontalAlign.Index,
                    textObj.LineSpacing[args.Frame] + LineSpacing[args.Frame]);

                stroke.DrawImage(
                    new(((stroke.Width - args.Value.Width) / 2) + (int)CenterX[args.Frame], ((stroke.Height - args.Value.Height) / 2) + (int)CenterY[args.Frame]),
                    args.Value);

                args.Value.Dispose();

                args.Value = stroke;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return CenterX;
            yield return CenterY;
            yield return LineSpacing;
            yield return Size;
            yield return Color;
        }
    }
}