// Polygon.cs
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
using BEditor.Primitive.Effects;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Get an <see cref="ImageObject"/> to draw a polygon.
    /// </summary>
    public sealed class Polygon : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Polygon, EaseProperty> WidthProperty = GL3DObject.WidthProperty.WithOwner<Polygon>(
            owner => owner.Width,
            (owner, obj) => owner.Width = obj);

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Polygon, EaseProperty> HeightProperty = GL3DObject.HeightProperty.WithOwner<Polygon>(
            owner => owner.Height,
            (owner, obj) => owner.Height = obj);

        /// <summary>
        /// Defines the <see cref="Number"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Polygon, ValueProperty> NumberProperty = EditingProperty.RegisterDirect<ValueProperty, Polygon>(
            nameof(Number),
            owner => owner.Number,
            (owner, obj) => owner.Number = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata("角", 3, Min: 3)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Polygon, ColorProperty> ColorProperty = ColorKey.ColorProperty.WithOwner<Polygon>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        public Polygon()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Polygon;

        /// <summary>
        /// Gest the width of the polygon.
        /// </summary>
        [AllowNull]
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Gets the height of the polygon.
        /// </summary>
        [AllowNull]
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the number of corners of a polygon.
        /// </summary>
        [AllowNull]
        public ValueProperty Number { get; private set; }

        /// <summary>
        /// Get the color of the polygon.
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
            yield return Number;
            yield return Color;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectApplyArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            return Image.Polygon((int)Number.Value, width, height, Color.Value);
        }
    }
}