﻿using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class Lighting : FilterEffect
{
    public static readonly CoreProperty<Color> MultiplyProperty;
    public static readonly CoreProperty<Color> AddProperty;
    private Color _multiply = Colors.White;
    private Color _add;

    static Lighting()
    {
        MultiplyProperty = ConfigureProperty<Color, Lighting>(nameof(Multiply))
            .Accessor(o => o.Multiply, (o, v) => o.Multiply = v)
            .DefaultValue(Colors.White)
            .Register();

        AddProperty = ConfigureProperty<Color, Lighting>(nameof(Add))
            .Accessor(o => o.Add, (o, v) => o.Add = v)
            .Register();

        AffectsRender<Lighting>(MultiplyProperty, AddProperty);
    }

    [Display(Name = nameof(Strings.Multiplication), ResourceType = typeof(Strings))]
    public Color Multiply
    {
        get => _multiply;
        set => SetAndRaise(MultiplyProperty, ref _multiply, value);
    }

    [Display(Name = nameof(Strings.Addition), ResourceType = typeof(Strings))]
    public Color Add
    {
        get => _add;
        set => SetAndRaise(AddProperty, ref _add, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Lighting(Multiply, Add);
    }
}
