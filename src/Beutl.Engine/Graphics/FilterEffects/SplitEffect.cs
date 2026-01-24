using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.SplitEquallyEffect), ResourceType = typeof(Strings))]
public partial class SplitEffect : FilterEffect
{
    public SplitEffect()
    {
        ScanProperties<SplitEffect>();
    }

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(Strings.HorizontalDivisions), ResourceType = typeof(Strings))]
    public IProperty<int> HorizontalDivisions { get; } = Property.CreateAnimatable(2);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(Strings.VerticalDivisions), ResourceType = typeof(Strings))]
    public IProperty<int> VerticalDivisions { get; } = Property.CreateAnimatable(2);

    [Display(Name = nameof(Strings.HorizontalSpacing), ResourceType = typeof(Strings))]
    public IProperty<float> HorizontalSpacing { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.VerticalSpacing), ResourceType = typeof(Strings))]
    public IProperty<float> VerticalSpacing { get; } = Property.CreateAnimatable(0f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (r.HorizontalDivisions, r.VerticalDivisions, r.HorizontalSpacing, r.VerticalSpacing),
            static (d, effectContext) =>
            {
                for (int i = 0; i < effectContext.Targets.Count; i++)
                {
                    EffectTarget t = effectContext.Targets[i];
                    RenderTarget renderTarget = t.RenderTarget!;

                    float divWidth = t.Bounds.Width / d.HorizontalDivisions;
                    float divHeight = t.Bounds.Height / d.VerticalDivisions;

                    if ((int)divWidth <= 0 || (int)divHeight <= 0)
                    {
                        t.Dispose();
                        effectContext.Targets.RemoveAt(i);
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
                                EffectTarget newTarget = effectContext.CreateTarget(
                                    new Rect(
                                        newBounds.X + (divWidth + d.HorizontalSpacing) * h,
                                        newBounds.Y + (divHeight + d.VerticalSpacing) * v,
                                        divWidth,
                                        divHeight));

                                using (ImmediateCanvas canvas = effectContext.Open(newTarget))
                                {
                                    canvas.Clear();
                                    canvas.DrawRenderTarget(renderTarget, new Point(-divWidth * h, -divHeight * v));
                                }

                                newTargets[v * d.HorizontalDivisions + h] = newTarget;
                            }
                        }

                        t.Dispose();
                        effectContext.Targets.RemoveAt(i);
                        effectContext.Targets.InsertRange(i, newTargets);
                        i += newTargets.Length - 1;
                    }
                }
            });
    }
}
