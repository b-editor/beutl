// RoundRect.cs
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
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> TopLeftRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundRect>(
            nameof(TopLeftRadius),
            owner => owner.TopLeftRadius,
            (owner, obj) => owner.TopLeftRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.TopLeft} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="TopRightRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> TopRightRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundRect>(
            nameof(TopRightRadius),
            owner => owner.TopRightRadius,
            (owner, obj) => owner.TopRightRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.TopRight} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="BottomLeftRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> BottomLeftRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundRect>(
            nameof(BottomLeftRadius),
            owner => owner.BottomLeftRadius,
            (owner, obj) => owner.BottomLeftRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.BottomLeft} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="BottomRightRadius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> BottomRightRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundRect>(
            nameof(BottomRightRadius),
            owner => owner.BottomRightRadius,
            (owner, obj) => owner.BottomRightRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.BottomRight} ({Strings.Radius})", 20, min: 0)).Serialize());

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
        public RoundRect()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.RoundRect;

        /// <summary>
        /// Get the width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty TopLeftRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty TopRightRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty BottomLeftRadius { get; private set; }

        /// <summary>
        /// Get the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty BottomRightRadius { get; private set; }

        /// <summary>
        /// Get the line width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Line { get; private set; }

        /// <summary>
        /// Get the type of the shape.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
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

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectApplyArgs args)
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