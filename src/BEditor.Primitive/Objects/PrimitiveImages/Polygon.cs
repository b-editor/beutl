using System.Collections.Generic;

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
        public static readonly DirectEditingProperty<Polygon, ValueProperty> NumberProperty = EditingProperty.RegisterSerializeDirect<ValueProperty, Polygon>(
            nameof(Number),
            owner => owner.Number,
            (owner, obj) => owner.Number = obj,
            new ValuePropertyMetadata("角", 3, Min: 3));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Polygon, ColorProperty> ColorProperty = ColorKey.ColorProperty.WithOwner<Polygon>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Iniitializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Polygon()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Polygon;

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
                yield return Number;
                yield return Color;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the polygon.
        /// </summary>
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the polygon.
        /// </summary>
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the <see cref="ValueProperty"/> representing the number of corners of a polygon.
        /// </summary>
        public ValueProperty Number { get; private set; }

        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color of the polygon.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            return Image.Polygon((int)Number.Value, width, height, Color.Value);
        }
    }
}