using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public sealed class GammaCorrection : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Gamma"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GammaCorrection, EaseProperty> GammaProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, GammaCorrection>(
            nameof(Gamma),
            owner => owner.Gamma,
            (owner, obj) => owner.Gamma = obj,
            new EasePropertyMetadata(Strings.Gamma, 100, 300, 1));

#pragma warning disable CS8618
        public GammaCorrection()
#pragma warning restore CS8618
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
            args.Value.Gamma(Gamma[args.Frame] / 100, Parent.Parent.DrawingContext);
        }
    }
#pragma warning restore CS1591
}