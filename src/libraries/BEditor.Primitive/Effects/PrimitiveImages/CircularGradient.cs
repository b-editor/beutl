// CircularGradient.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.LangResources;

using Reactive.Bindings;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that masks an image with a circular gradient.
    /// </summary>
    public sealed class CircularGradient : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectProperty<CircularGradient, EaseProperty> CenterXProperty = Coordinate.CenterXProperty.WithOwner<CircularGradient>(
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj);

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectProperty<CircularGradient, EaseProperty> CenterYProperty = Coordinate.CenterYProperty.WithOwner<CircularGradient>(
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj);

        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectProperty<CircularGradient, EaseProperty> RadiusProperty = EditingProperty.RegisterDirect<EaseProperty, CircularGradient>(
            nameof(Radius),
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Radius, 100)).Serialize());

        /// <summary>
        /// Defines the <see cref="Gradient"/> property.
        /// </summary>
        public static readonly DirectProperty<CircularGradient, GradientProperty> GradientProperty = LinearGradient.GradientProperty.WithOwner<CircularGradient>(
            owner => owner.Gradient,
            (owner, obj) => owner.Gradient = obj);

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectProperty<CircularGradient, SelectorProperty> ModeProperty = LinearGradient.ModeProperty.WithOwner<CircularGradient>(
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularGradient"/> class.
        /// </summary>
        public CircularGradient()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.CircularGradient;

        /// <summary>
        /// Gets the X coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the radius.
        /// </summary>
        [AllowNull]
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets the gradient.
        /// </summary>
        [AllowNull]
        public GradientProperty Gradient { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> that selects the gradient mode.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;

            args.Value.CircularGradient(
                new PointF(CenterX[f], CenterY[f]),
                Radius[f],
                Gradient.KeyPoints.Select(i => i.Color),
                Gradient.KeyPoints.Select(i => i.Position),
                LinearGradient._tiles[Mode.Index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return CenterX;
            yield return CenterY;
            yield return Radius;
            yield return Gradient;
            yield return Mode;
        }

        /// <inheritdoc/>
        public override void SetObjectData(DeserializeContext context)
        {
            base.SetObjectData(context);
            var element = context.Element;

            if (element.TryGetProperty("Colors", out var colors) &&
                element.TryGetProperty("Anchors", out var anchors))
            {
                var colorsArray = colors.GetProperty("Value").GetString()?.Split(',')?.Select(i => Color.Parse(i));
                var anchorsArray = anchors.GetProperty("Value").GetString()?.Split(',')?.Select(i => float.Parse(i));

                if (colorsArray != null && anchorsArray != null)
                {
                    Gradient.KeyPoints.Clear();
                    foreach (var (color, anchor) in colorsArray.Zip(anchorsArray))
                    {
                        Gradient.KeyPoints.Add(new GradientKeyPoint(color, anchor));
                    }
                }
            }
        }
    }
}