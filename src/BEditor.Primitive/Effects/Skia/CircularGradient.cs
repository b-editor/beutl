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
using BEditor.Primitive.Resources;

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
        public static readonly DirectEditingProperty<CircularGradient, EaseProperty> CenterXProperty = Coordinate.CenterXProperty.WithOwner<CircularGradient>(
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj);

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CircularGradient, EaseProperty> CenterYProperty = Coordinate.CenterYProperty.WithOwner<CircularGradient>(
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj);

        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CircularGradient, EaseProperty> RadiusProperty = EditingProperty.RegisterDirect<EaseProperty, CircularGradient>(
            nameof(Radius),
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Radius, 100)).Serialize());

        /// <summary>
        /// Defines the <see cref="Colors"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CircularGradient, TextProperty> ColorsProperty = LinearGradient.ColorsProperty.WithOwner<CircularGradient>(
            owner => owner.Colors,
            (owner, obj) => owner.Colors = obj);

        /// <summary>
        /// Defines the <see cref="Anchors"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CircularGradient, TextProperty> AnchorsProperty = LinearGradient.AnchorsProperty.WithOwner<CircularGradient>(
            owner => owner.Anchors,
            (owner, obj) => owner.Anchors = obj);

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CircularGradient, SelectorProperty> ModeProperty = LinearGradient.ModeProperty.WithOwner<CircularGradient>(
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj);

        private ReactiveProperty<Color[]>? _colorsProp;

        private ReactiveProperty<float[]>? _pointsProp;

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
        /// Gets the colors.
        /// </summary>
        [AllowNull]
        public TextProperty Colors { get; private set; }

        /// <summary>
        /// Gets the anchors.
        /// </summary>
        [AllowNull]
        public TextProperty Anchors { get; private set; }

        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the gradient mode.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mode { get; private set; }

        private ReactiveProperty<Color[]> ColorsProp => _colorsProp ??= new();

        private ReactiveProperty<float[]> PointsProp => _pointsProp ??= new();

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
            var colors = ColorsProp.Value;
            var points = PointsProp.Value;

            // LinearGradient参照
            while (colors.Length != points.Length)
            {
                if (colors.Length < points.Length)
                {
                    colors = colors.Append(default).ToArray();
                }
                else if (colors.Length > points.Length)
                {
                    points = points.Append(default).ToArray();
                }
            }

            args.Value.CircularGradient(
                new PointF(CenterX[f], CenterY[f]),
                Radius[f],
                colors,
                points,
                LinearGradient._tiles[Mode.Index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return CenterX;
            yield return CenterY;
            yield return Radius;
            yield return Colors;
            yield return Anchors;
            yield return Mode;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _colorsProp = Colors
                .Select(str =>
                    str.Replace(" ", string.Empty)
                        .Split(',')
                        .Select(s => Color.FromHTML(s))
                        .ToArray())
                .ToReactiveProperty()!;

            _pointsProp = Anchors
                .Select(str =>
                    str.Replace(" ", string.Empty)
                        .Split(',')
                        .Where(s => float.TryParse(s, out _))
                        .Select(s => float.Parse(s))
                        .ToArray())
                .ToReactiveProperty()!;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _colorsProp?.Dispose();
            _pointsProp?.Dispose();
        }
    }
}