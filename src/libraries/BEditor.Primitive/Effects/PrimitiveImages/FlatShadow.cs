// FlatShadow.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// The flat shadow.
    /// </summary>
    public sealed class FlatShadow : ImageEffect
    {
        public static readonly DirectProperty<FlatShadow, EaseProperty> AngleProperty
            = EditingProperty.RegisterDirect<EaseProperty, FlatShadow>(
                nameof(Angle),
                owner => owner.Angle,
                (owner, obj) => owner.Angle = obj,
                EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Angle", 45, float.NaN, float.NaN)).Serialize());

        public static readonly DirectProperty<FlatShadow, EaseProperty> LengthProperty
            = EditingProperty.RegisterDirect<EaseProperty, FlatShadow>(
                nameof(Length),
                owner => owner.Length,
                (owner, obj) => owner.Length = obj,
                EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Length", 200, float.NaN, 0)).Serialize());

        /// <inheritdoc/>
        public override string Name => "Flat shadow";

        public EaseProperty Angle { get; private set; }

        public EaseProperty Length { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var tmp = args.Value.FlatShadow(Colors.White, Angle[args.Frame], Length[args.Frame]);
            args.Value.Dispose();
            args.Value = tmp;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Angle;
            yield return Length;
        }
    }
}
