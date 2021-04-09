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
    public sealed class RGBColor : ImageEffect
    {
        public static readonly EditingProperty<EaseProperty> RedProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RGBColor>(
            nameof(Red),
            owner => owner.Red,
            (owner, obj) => owner.Red = obj,
            new EasePropertyMetadata(Strings.Red, 0, 255, -255));
        public static readonly EditingProperty<EaseProperty> GreenProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RGBColor>(
            nameof(Green),
            owner => owner.Green,
            (owner, obj) => owner.Green = obj,
            new EasePropertyMetadata(Strings.Green, 0, 255, -255));
        public static readonly EditingProperty<EaseProperty> BlueProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, RGBColor>(
            nameof(Blue),
            owner => owner.Blue,
            (owner, obj) => owner.Blue = obj,
            new EasePropertyMetadata(Strings.Blue, 0, 255, -255));

        public RGBColor()
        {
        }

        public override string Name => Strings.RGBColorCorrection;
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
            args.Value.RGBColor(
                (short)Red[args.Frame],
                (short)Green[args.Frame],
                (short)Blue[args.Frame]);
        }
    }
#pragma warning restore CS1591
}
