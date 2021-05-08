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
    public sealed class ContrastCorrection : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> ContrastProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, ContrastCorrection>(
            nameof(Contrast),
            owner => owner.Contrast,
            (owner, obj) => owner.Contrast = obj,
            new EasePropertyMetadata(Strings.Contrast, 0, 255, -255));

        public ContrastCorrection()
        {
        }

        public override string Name => Strings.ContrastCorrection;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Contrast
        };
        public EaseProperty Contrast { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Contrast((short)Contrast[args.Frame]);
        }
    }
#pragma warning restore CS1591
}