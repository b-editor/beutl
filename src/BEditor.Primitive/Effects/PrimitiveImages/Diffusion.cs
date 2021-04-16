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
    public sealed class Diffusion : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Value"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Diffusion, EaseProperty> ValueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Diffusion>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            new EasePropertyMetadata(Strings.ThresholdValue, 7, 30, 0));

#pragma warning disable CS8618
        public Diffusion()
#pragma warning restore CS8618
        {
        }

        public override string Name => Strings.Diffusion;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Value
        };
        public EaseProperty Value { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Diffusion((byte)Value[args.Frame]);
        }
    }
#pragma warning restore CS1591
}