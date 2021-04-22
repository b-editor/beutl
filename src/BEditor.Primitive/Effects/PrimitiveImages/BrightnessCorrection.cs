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
    public sealed class BrightnessCorrection : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> BrightnessProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, BrightnessCorrection>(
            nameof(Brightness),
            owner => owner.Brightness,
            (owner, obj) => owner.Brightness = obj,
            new EasePropertyMetadata(Strings.Brightness, 0, 255, -255));

        public BrightnessCorrection()
        {
        }

        public override string Name => Strings.BrightnessCorrection;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Brightness
        };
        public EaseProperty Brightness { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var context = Parent.Parent.DrawingContext;

            if (context is not null && Settings.Default.PrioritizeGPU)
            {
                args.Value.Brightness(context, (short)Brightness[args.Frame]);
            }
            else
            {
                args.Value.Brightness((short)Brightness[args.Frame]);
            }
        }
    }
#pragma warning restore CS1591
}