using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public sealed class Noise : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Value"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Noise, EaseProperty> ValueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Noise>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            new EasePropertyMetadata(Strings.ThresholdValue, 30, 255, 0));

#pragma warning disable CS8618
        public Noise()
#pragma warning restore CS8618
        {
        }

        public override string Name => Strings.Noise;

        public EaseProperty Value { get; set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Noise((byte)Value[args.Frame]);
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Value;
        }
    }
#pragma warning restore CS1591
}