using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

using Beutl.Language;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class SplitEffect : FilterEffect
{
    public static readonly CoreProperty<int> HorizontalDivisionsProperty;
    public static readonly CoreProperty<int> VerticalDivisionsProperty;
    public static readonly CoreProperty<float> HorizontalSpacingProperty;
    public static readonly CoreProperty<float> VerticalSpacingProperty;

    static SplitEffect()
    {
        HorizontalDivisionsProperty = ConfigureProperty<int, SplitEffect>(nameof(HorizontalDivisions))
            .DefaultValue(2)
            .Register();

        VerticalDivisionsProperty = ConfigureProperty<int, SplitEffect>(nameof(VerticalDivisions))
            .DefaultValue(2)
            .Register();

        HorizontalSpacingProperty = ConfigureProperty<float, SplitEffect>(nameof(HorizontalSpacing))
            .DefaultValue(0)
            .Register();

        VerticalSpacingProperty = ConfigureProperty<float, SplitEffect>(nameof(VerticalSpacing))
            .DefaultValue(0)
            .Register();

        AffectsRender<SplitEffect>(
            HorizontalDivisionsProperty,
            VerticalDivisionsProperty,
            HorizontalSpacingProperty,
            VerticalSpacingProperty);
    }

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(Strings.HorizontalDivisions), ResourceType = typeof(Strings))]
    public int HorizontalDivisions
    {
        get => GetValue(HorizontalDivisionsProperty);
        set => SetValue(HorizontalDivisionsProperty, value);
    }

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(Strings.VerticalDivisions), ResourceType = typeof(Strings))]
    public int VerticalDivisions
    {
        get => GetValue(VerticalDivisionsProperty);
        set => SetValue(VerticalDivisionsProperty, value);
    }

    [Display(Name = nameof(Strings.HorizontalSpacing), ResourceType = typeof(Strings))]
    public float HorizontalSpacing
    {
        get => GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    [Display(Name = nameof(Strings.VerticalSpacing), ResourceType = typeof(Strings))]
    public float VerticalSpacing
    {
        get => GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((HorizontalDivisions, VerticalDivisions, HorizontalSpacing, VerticalSpacing), (d, context) =>
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget t = context.Targets[i];
                SKSurface surface = t.Surface!.Value;

                float divWidth = t.Bounds.Width / d.HorizontalDivisions;
                float divHeight = t.Bounds.Height / d.VerticalDivisions;

                if ((int)divWidth <= 0 || (int)divHeight <= 0)
                {
                    t.Dispose();
                    context.Targets.RemoveAt(i);
                    i--;
                }
                else
                {
                    var newBounds = new Rect(
                        0,
                        0,
                        t.Bounds.Width + (d.HorizontalSpacing * (d.HorizontalDivisions - 1)),
                        t.Bounds.Height + (d.VerticalSpacing * (d.VerticalDivisions - 1)));
                    newBounds = t.Bounds.CenterRect(newBounds);

                    var newTargets = new EffectTarget[d.HorizontalDivisions * d.VerticalDivisions];

                    for (int v = 0; v < d.VerticalDivisions; v++)
                    {
                        for (int h = 0; h < d.HorizontalDivisions; h++)
                        {
                            float hh = t.Bounds.Width / -d.VerticalDivisions;
                            float vv = t.Bounds.Height / -d.HorizontalDivisions;
                            EffectTarget newTarget = context.CreateTarget(
                                new Rect(
                                    newBounds.X + (divWidth + d.HorizontalSpacing) * h,
                                    newBounds.Y + (divHeight + d.VerticalSpacing) * v,
                                    divWidth,
                                    divHeight));

                            using (ImmediateCanvas canvas = context.Open(newTarget))
                            {
                                canvas.DrawSurface(surface, new Point(-divWidth * h, -divHeight * v));
                            }

                            newTargets[v * d.HorizontalDivisions + h] = newTarget;
                        }
                    }

                    t.Dispose();
                    context.Targets.RemoveAt(i);
                    context.Targets.InsertRange(i, newTargets);
                    i += newTargets.Length - 1;
                }
            }
        });
    }
}
