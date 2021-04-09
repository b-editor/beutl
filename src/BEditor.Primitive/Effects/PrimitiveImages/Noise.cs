using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

#nullable disable

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public sealed class Noise : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> ValueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Noise>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            new EasePropertyMetadata(Strings.ThresholdValue, 30, 255, 0));

        public Noise()
        {
        }

        public override string Name => Strings.Noise;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Value
        };
        public EaseProperty Value { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Noise((byte)Value[args.Frame]);
        }
    }
#pragma warning restore CS1591
}
