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
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<RoundRect, EaseProperty> RadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RoundRect>(
            nameof(Radius),
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj,
            new EasePropertyMetadata(Strings.Radius, 20, Min: 0));

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
                yield return Radius;
                yield return Line;
                yield return Color;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the roundness of a shape.
        /// </summary>
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the line width of the shape.
        /// </summary>
        public EaseProperty Line { get; private set; }

        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the type of the shape.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var f = args.Frame;
            var r = (int)Radius[f];
            return Image.RoundRect((int)Width[f], (int)Height[f], (int)Line[f], r, r, Color.Value);
        }
    }
}