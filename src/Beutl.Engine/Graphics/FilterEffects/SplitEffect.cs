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

        Action<ISplitEmitter> emit = emitter => EmitTiles(emitter, hDiv, vDiv, hSpacing, vSpacing);
        long branchCount = (long)hDiv * vDiv;
        bool hasRenderableStaticBranches = hDiv > 0
            && vDiv > 0
            && builder.Bounds.Width / hDiv >= 1f
            && builder.Bounds.Height / vDiv >= 1f
            && branchCount <= int.MaxValue;

        // The division counts are structural when the branches can be declared safely (C3.6). A split whose
        // logical tiles are sub-pixel emits no branches, so keeping it dynamic avoids allocating a massive static
        // resource plan for outputs that can never exist. The dynamic form also avoids overflowing the declaration
        // count for serialized division values whose product exceeds Int32.
        builder.Split(hasRenderableStaticBranches
            ? SplitNodeDescriptor.Static(emit, (int)branchCount, structuralToken: nameof(SplitEffect))
            : SplitNodeDescriptor.Dynamic(emit, structuralToken: nameof(SplitEffect)));
    }

    private static void EmitTiles(ISplitEmitter emitter, int hDiv, int vDiv, float hSpacing, float vSpacing)
    {
        if (hDiv <= 0 || vDiv <= 0)
            return;

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
}
