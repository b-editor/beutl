using System.ComponentModel.DataAnnotations;

using OpenCvSharp;

namespace Beutl.Graphics.Effects;

public class Negaposi : FilterEffect
{
    public static readonly CoreProperty<byte> RedProperty;
    public static readonly CoreProperty<byte> GreenProperty;
    public static readonly CoreProperty<byte> BlueProperty;
    public static readonly CoreProperty<float> StrengthProperty;
    private byte _red;
    private byte _green;
    private byte _blue;
    private float _strength = 100;

    static Negaposi()
    {
        RedProperty = ConfigureProperty<byte, Negaposi>(nameof(Red))
            .Accessor(o => o.Red, (o, v) => o.Red = v)
            .Register();

        GreenProperty = ConfigureProperty<byte, Negaposi>(nameof(Green))
            .Accessor(o => o.Green, (o, v) => o.Green = v)
            .Register();

        BlueProperty = ConfigureProperty<byte, Negaposi>(nameof(Blue))
            .Accessor(o => o.Blue, (o, v) => o.Blue = v)
            .Register();

        StrengthProperty = ConfigureProperty<float, Negaposi>(nameof(Strength))
            .Accessor(o => o.Strength, (o, v) => o.Strength = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<Negaposi>(RedProperty, GreenProperty, BlueProperty, StrengthProperty);
    }

    public byte Red
    {
        get => _red;
        set => SetAndRaise(RedProperty, ref _red, value);
    }

    public byte Green
    {
        get => _green;
        set => SetAndRaise(GreenProperty, ref _green, value);
    }

    public byte Blue
    {
        get => _blue;
        set => SetAndRaise(BlueProperty, ref _blue, value);
    }

    [Range(0, 100)]
    public float Strength
    {
        get => _strength;
        set => SetAndRaise(StrengthProperty, ref _strength, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.LookupTable(
            (_red, _green, _blue),
            _strength / 100,
            ((byte r, byte g, byte b) data, (byte[] A, byte[] R, byte[] G, byte[] B) array) =>
            {
                LookupTable.Linear(array.A);
                LookupTable.Negaposi((array.R, array.G, array.B), data.r, data.g, data.b);
            });
    }
}
