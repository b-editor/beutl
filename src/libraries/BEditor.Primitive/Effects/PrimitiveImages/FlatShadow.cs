// FlatShadow.cs
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

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// The flat shadow.
    /// </summary>
    public sealed class FlatShadow : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Angle"/> property.
        /// </summary>
        public static readonly DirectProperty<FlatShadow, EaseProperty> AngleProperty
            = EditingProperty.RegisterDirect<EaseProperty, FlatShadow>(
                nameof(Angle),
                owner => owner.Angle,
                (owner, obj) => owner.Angle = obj,
                EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Angle, 45, float.NaN, float.NaN)).Serialize());

        /// <summary>
        /// Defines the <see cref="Length"/> property.
        /// </summary>
        public static readonly DirectProperty<FlatShadow, EaseProperty> LengthProperty
            = EditingProperty.RegisterDirect<EaseProperty, FlatShadow>(
                nameof(Length),
                owner => owner.Length,
                (owner, obj) => owner.Length = obj,
                EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Length, 200, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<FlatShadow, ColorProperty> ColorProperty
            = EditingProperty.RegisterDirect<ColorProperty, FlatShadow>(
                nameof(Color),
                owner => owner.Color,
                (owner, obj) => owner.Color = obj,
                EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.White)).Serialize());

        /// <inheritdoc/>
        public override string Name => Strings.FlatShadow;

        /// <summary>
        /// Gets the angle.
        /// </summary>
        [AllowNull]
        public EaseProperty Angle { get; private set; }

        /// <summary>
        /// Gets the shadow length.
        /// </summary>
        [AllowNull]
        public EaseProperty Length { get; private set; }

        /// <summary>
        /// Gets the color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var length = Length[args.Frame];
            var angle = Angle[args.Frame];
            var tmp = args.Value.FlatShadow(Color.Value, angle, length);
            args.Value.Dispose();
            args.Value = tmp;

            var radian = angle * (MathF.PI / 180);
            var x2 = (int)(length * MathF.Cos(radian));
            var y2 = (int)(length * MathF.Sin(radian));

            if (Parent.Effect[0] is ImageObject imgObj)
            {
                imgObj.Coordinate.CenterX.Optional += x2 / 2;
                imgObj.Coordinate.CenterY.Optional -= y2 / 2;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Angle;
            yield return Length;
            yield return Color;
        }
    }
}
