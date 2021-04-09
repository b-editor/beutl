using System;
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
    public sealed class Negaposi : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> RedProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Negaposi>(
            nameof(Red),
            owner => owner.Red,
            (owner, obj) => owner.Red = obj,
            new EasePropertyMetadata(Strings.Red, 255, 255, 0));
        public static readonly EditingProperty<EaseProperty> GreenProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Negaposi>(
            nameof(Green),
            owner => owner.Green,
            (owner, obj) => owner.Green = obj,
            new EasePropertyMetadata(Strings.Green, 255, 255, 0));
        public static readonly EditingProperty<EaseProperty> BlueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Negaposi>(
            nameof(Blue),
            owner => owner.Blue,
            (owner, obj) => owner.Blue = obj,
            new EasePropertyMetadata(Strings.Blue, 255, 255, 0));

        public Negaposi()
        {
        }

        public override string Name => Strings.Negaposi;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Red,
            Green,
            Blue
        };
        public EaseProperty Red { get; set; }
        public EaseProperty Green { get; set; }
        public EaseProperty Blue { get; set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Negaposi(
                (byte)Red[args.Frame],
                (byte)Green[args.Frame],
                (byte)Blue[args.Frame]);
        }
    }
#pragma warning restore CS1591
}
