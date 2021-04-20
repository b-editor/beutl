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
        public static readonly DirectEditingProperty<Shape, EaseProperty> LineProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Shape>(
            nameof(Line),
            owner => owner.Line,
            (owner, obj) => owner.Line= obj,
            new EasePropertyMetadata(Strings.LineWidth, 4000, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, ColorProperty> ColorProperty = Polygon.ColorProperty.WithOwner<Shape>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Defines the <see cref="Type"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Shape, SelectorProperty> TypeProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, Shape>(
            nameof(Type),
            owner => owner.Type,
            (owner, obj) => owner.Type = obj,
            new SelectorPropertyMetadata(Strings.Type, new string[]
            {
                Strings.Ellipse,
                Strings.Rectangle
            }));

        /// <summary>
        /// Initializes a new instance of the <see cref="Shape"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Shape()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Shape;

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
                yield return Line;
                yield return Color;
                yield return Type;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the shape.
        /// </summary>
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the line width of the shape.
        /// </summary>
        public EaseProperty Line { get; private set; }

        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color of the shape.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the type of the shape.
        /// </summary>
        public SelectorProperty Type { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
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