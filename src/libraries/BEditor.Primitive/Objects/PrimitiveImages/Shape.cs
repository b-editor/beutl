// Shape.cs
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
    /// Represents an <see cref="ImageObject"/> to draw a shape.
    /// </summary>
    public sealed class Shape : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, EaseProperty> WidthProperty = Polygon.WidthProperty.WithOwner<Shape>(
            owner => owner.Width,
            (owner, obj) => owner.Width = obj);

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, EaseProperty> HeightProperty = Polygon.HeightProperty.WithOwner<Shape>(
            owner => owner.Height,
            (owner, obj) => owner.Height = obj);

        /// <summary>
        /// Defines the <see cref="Line"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, EaseProperty> LineProperty = EditingProperty.RegisterDirect<EaseProperty, Shape>(
            nameof(Line),
            owner => owner.Line,
            (owner, obj) => owner.Line = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.LineWidth, 4000, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, ColorProperty> ColorProperty = Polygon.ColorProperty.WithOwner<Shape>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Defines the <see cref="Type"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, SelectorProperty> TypeProperty = EditingProperty.RegisterDirect<SelectorProperty, Shape>(
            nameof(Type),
            owner => owner.Type,
            (owner, obj) => owner.Type = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Type, new string[]
            {
                Strings.Ellipse,
                Strings.Rectangle,
            })).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Shape"/> class.
        /// </summary>
        public Shape()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Shape;

        /// <summary>
        /// Gets the width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Gets the height of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the line width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty Line { get; private set; }

        /// <summary>
        /// Gets the color of the shape.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> to select the type of the shape.
        /// </summary>
        [AllowNull]
        public SelectorProperty Type { get; private set; }

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
            yield return Line;
            yield return Color;
            yield return Type;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectApplyArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            if (Type.Index == 0)
            {
                return Image.Ellipse(width, height, (int)Line[args.Frame], Color.Value);
            }
            else
            {
                return Image.Rect(width, height, (int)Line[args.Frame], Color.Value);
            }
        }
    }
}