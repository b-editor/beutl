// Arc.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// The arc.
    /// </summary>
    public sealed class Arc : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, EaseProperty> WidthProperty = Shape.WidthProperty.WithOwner<Arc>(
            owner => owner.Width,
            (owner, obj) => owner.Width = obj);

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, EaseProperty> HeightProperty = Shape.HeightProperty.WithOwner<Arc>(
            owner => owner.Height,
            (owner, obj) => owner.Height = obj);

        /// <summary>
        /// Defines the <see cref="Line"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, EaseProperty> LineProperty = Shape.LineProperty.WithOwner<Arc>(
            owner => owner.Line,
            (owner, obj) => owner.Line = obj);

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, ColorProperty> ColorProperty = Shape.ColorProperty.WithOwner<Arc>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Defines the <see cref="StartAngle"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, EaseProperty> StartAngleProperty = EditingProperty.RegisterDirect<EaseProperty, Arc>(
            nameof(StartAngle),
            owner => owner.StartAngle,
            (owner, obj) => owner.StartAngle = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.StartAngle, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="SweepAngle"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, EaseProperty> SweepAngleProperty = EditingProperty.RegisterDirect<EaseProperty, Arc>(
            nameof(SweepAngle),
            owner => owner.SweepAngle,
            (owner, obj) => owner.SweepAngle = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SweepAngle, 360)).Serialize());

        /// <summary>
        /// Defines the <see cref="SweepAngle"/> property.
        /// </summary>
        public static readonly DirectProperty<Arc, CheckProperty> UseCenterProperty = EditingProperty.RegisterDirect<CheckProperty, Arc>(
            nameof(UseCenter),
            owner => owner.UseCenter,
            (owner, obj) => owner.UseCenter = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.UseCenter)).Serialize());

        /// <inheritdoc/>
        public override string Name => Strings.Arc;

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
        /// Gets the line width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty StartAngle { get; private set; }

        /// <summary>
        /// Gets the line width of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty SweepAngle { get; private set; }

        /// <summary>
        /// Gets the line width of the shape.
        /// </summary>
        [AllowNull]
        public CheckProperty UseCenter { get; private set; }

        /// <summary>
        /// Gets the color of the shape.
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
            yield return Line;
            yield return StartAngle;
            yield return SweepAngle;
            yield return UseCenter;
            yield return Color;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectApplyArgs args)
        {
            var width = Width[args.Frame];
            var height = Height[args.Frame];
            var line = Line[args.Frame];
            var startAngle = StartAngle[args.Frame];
            var sweepAngle = SweepAngle[args.Frame];

            return Image.Arc((int)width, (int)height, startAngle, sweepAngle, UseCenter.Value, new Brush()
            {
                Color = Color.Value,
                StrokeWidth = (int)line,
                Style = BrushStyle.Stroke,
            });
        }
    }
}
