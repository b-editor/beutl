using System;
using System.Collections.Generic;
using System.Security.Cryptography;

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
    public sealed class GammaCorrection : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> GammaProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, GammaCorrection>(
            nameof(Gamma),
            owner => owner.Gamma,
            (owner, obj) => owner.Gamma = obj,
            new EasePropertyMetadata(Strings.Gamma, 100, 300, 1));

        public GammaCorrection()
        {
        }

        public override string Name => Strings.GammaCorrection;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Gamma
        };
        public EaseProperty Gamma { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Gamma(Gamma[args.Frame] / 100);
        }
    }
#pragma warning restore CS1591
}
