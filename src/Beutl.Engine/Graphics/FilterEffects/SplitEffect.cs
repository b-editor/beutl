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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        int hDiv = r.HorizontalDivisions;
        int vDiv = r.VerticalDivisions;
        float hSpacing = r.HorizontalSpacing;
        float vSpacing = r.VerticalSpacing;

        // The division counts are structural: animating them recompiles exactly once (C3.6).
        builder.Split(SplitNodeDescriptor.Static(
            emitter => EmitTiles(emitter, hDiv, vDiv, hSpacing, vSpacing),
            branchCount: Math.Max(1, hDiv * vDiv),
            structuralToken: nameof(SplitEffect)));
    }

    private static void EmitTiles(ISplitEmitter emitter, int hDiv, int vDiv, float hSpacing, float vSpacing)
    {
        EffectInput input = emitter.Input;
        Rect bounds = input.Bounds;
        float w = emitter.WorkingScale;

        float divWidth = bounds.Width / hDiv;
        float divHeight = bounds.Height / vDiv;
        if ((int)divWidth <= 0 || (int)divHeight <= 0)
            return;

        var spacedBounds = new Rect(
            0, 0,
            bounds.Width + (hSpacing * (hDiv - 1)),
            bounds.Height + (vSpacing * (vDiv - 1)));
        spacedBounds = bounds.CenterRect(spacedBounds);

        for (int v = 0; v < vDiv; v++)
        {
            for (int h = 0; h < hDiv; h++)
            {
                var tileBounds = new Rect(
                    spacedBounds.X + (divWidth + hSpacing) * h,
                    spacedBounds.Y + (divHeight + vSpacing) * v,
                    divWidth,
                    divHeight);
                int hh = h;
                int vv = v;
                emitter.Emit(tileBounds, session =>
                {
                    ImmediateCanvas canvas = session.OpenCanvas();
                    using (canvas.PushDeviceSpace())
                    {
                        input.Draw(canvas, new Point(-divWidth * hh * w, -divHeight * vv * w));
                    }
                });
            }
        }
    }

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
                    // Per-tile crop offset scales by working density.
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

                                // Crop offset is device px; draw in device space.
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
