using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that draws a rectangle with rounded corners.
    /// </summary>
    public sealed class RoundRect : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> WidthProperty = Shape.WidthProperty.WithOwner<RoundRect>(
            owner => owner.Width,
            (owner, obj) => owner.Width = obj);

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> HeightProperty = Shape.HeightProperty.WithOwner<RoundRect>(
            owner => owner.Height,
            (owner, obj) => owner.Height = obj);

        /// <summary>
        /// Defines the <see cref="TopLeftRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> TopLeftRadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RoundRect>(
            nameof(TopLeftRadius),
            owner => owner.TopLeftRadius,
            (owner, obj) => owner.TopLeftRadius = obj,
            new EasePropertyMetadata($"{Strings.TopLeft} ({Strings.Radius})", 20, Min: 0));

        /// <summary>
        /// Defines the <see cref="TopRightRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> TopRightRadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RoundRect>(
            nameof(TopRightRadius),
            owner => owner.TopRightRadius,
            (owner, obj) => owner.TopRightRadius = obj,
            new EasePropertyMetadata($"{Strings.TopRight} ({Strings.Radius})", 20, Min: 0));

        /// <summary>
        /// Defines the <see cref="BottomLeftRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> BottomLeftRadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RoundRect>(
            nameof(BottomLeftRadius),
            owner => owner.BottomLeftRadius,
            (owner, obj) => owner.BottomLeftRadius = obj,
            new EasePropertyMetadata($"{Strings.BottomLeft} ({Strings.Radius})", 20, Min: 0));

        /// <summary>
        /// Defines the <see cref="BottomRightRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> BottomRightRadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RoundRect>(
            nameof(BottomRightRadius),
            owner => owner.BottomRightRadius,
            (owner, obj) => owner.BottomRightRadius = obj,
            new EasePropertyMetadata($"{Strings.BottomRight} ({Strings.Radius})", 20, Min: 0));

        /// <summary>
        /// Defines the <see cref="Line"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> LineProperty = Shape.LineProperty.WithOwner<RoundRect>(
            owner => owner.Line,
            (owner, obj) => owner.Line = obj);

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, ColorProperty> ColorProperty = Shape.ColorProperty.WithOwner<RoundRect>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRect"/> class.
        /// </summary>
#pragma warning disable CS8618
        public RoundRect()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.RoundRect;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Coordinate;
                yield return Scale;
                yield return Blend;
                yield return Rotate;
                yield return Material;
                yield return Width;
                yield return Height;
                yield return TopLeftRadius;
                yield return TopRightRadius;
                yield return BottomLeftRadius;
                yield return BottomRightRadius;
                yield return Line;
                yield return Color;
            }
        }

        /// <summary>
        /// Get the width of the shape.
        /// </summary>
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the width of the shape.
        /// </summary>
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        public EaseProperty TopLeftRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        public EaseProperty TopRightRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        public EaseProperty BottomLeftRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        public EaseProperty BottomRightRadius { get; private set; }

        /// <summary>
        /// Get the line width of the shape.
        /// </summary>
        public EaseProperty Line { get; private set; }

        /// <summary>
        /// Get the type of the shape.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var f = args.Frame;
            return Image.RoundRect(
                (int)Width[f],
                (int)Height[f],
                (int)Line[f],
                Color.Value,
                (int)TopLeftRadius[f],
                (int)TopRightRadius[f],
                (int)BottomLeftRadius[f],
                (int)BottomRightRadius[f]);
        }
    }
}