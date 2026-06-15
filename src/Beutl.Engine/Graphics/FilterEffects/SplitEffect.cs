using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.SplitEffect), ResourceType = typeof(GraphicsStrings))]
public partial class SplitEffect : FilterEffect
{
    public SplitEffect()
    {
        ScanProperties<SplitEffect>();
    }

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(GraphicsStrings.SplitEffect_HorizontalDivisions), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> HorizontalDivisions { get; } = Property.CreateAnimatable(2);

    [Range(1, int.MaxValue)]
    [Display(Name = nameof(GraphicsStrings.SplitEffect_VerticalDivisions), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> VerticalDivisions { get; } = Property.CreateAnimatable(2);

    [Display(Name = nameof(GraphicsStrings.SplitEffect_HorizontalSpacing), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> HorizontalSpacing { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.SplitEffect_VerticalSpacing), ResourceType = typeof(GraphicsStrings))]
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
                    // feature 003: the source is point-blitted into each ceil(tile × w) device tile, so the
                    // per-tile crop offset scales by w. w == 1 = pre-feature path. The bare WorkingScale is
                    // clamp-safe here: each tile sub-divides the already-allocatable source bounds, so
                    // CreateTarget's FR-037(b) clamp returns w unchanged, unlike bounds-inflating effects.
                    float w = effectContext.WorkingScale;

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

                                // feature 003: the crop offset is DEVICE px (× w), so enter device space —
                                // Open's base CTM CreateScale(w) would otherwise re-scale it. w == 1 = no-op.
                                using (ImmediateCanvas canvas = effectContext.Open(newTarget))
                                using (canvas.PushDeviceSpace())
                                {
                                    canvas.Clear();
                                    canvas.DrawRenderTarget(renderTarget, new Point(-divWidth * h * w, -divHeight * v * w));
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
