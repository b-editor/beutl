using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.DisplacementMapEffect), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapEffect : FilterEffect
{
    public DisplacementMapEffect()
    {
        ScanProperties<DisplacementMapEffect>();

        DisplacementMap.CurrentValue = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Colors.Transparent, 1)
            }
        };

        Transform.CurrentValue = new DisplacementMapTranslateTransform();
    }

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_DisplacementMap), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> DisplacementMap { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_SpreadMethod), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateAnimatable(GradientSpreadMethod.Pad);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_Channel), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DisplacementMapChannel> Channel { get; } = Property.Create(DisplacementMapChannel.Alpha);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_Signed), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Signed { get; } = Property.Create(false);

    [Display(Name = nameof(GraphicsStrings.DisplacementMapEffect_ShowDisplacementMap), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ShowDisplacementMap { get; } = Property.CreateAnimatable(false);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        Brush.Resource? displacementMap = r.DisplacementMap;
        if (displacementMap is null) return;

        if (r.ShowDisplacementMap)
        {
            // The map-preview path draws the displacement brush over the identity output rect (research D7: the mask
            // composition part is a geometry node); the executor owns the buffer, clear and sync.
            builder.Geometry(GeometryNodeDescriptor.Create(
                session => DrawDisplacementMap(session, displacementMap),
                BoundsContract.Identity,
                structuralToken: nameof(DisplacementMapEffect) + ".Show"));
        }
        else if (r.Transform is { } transform)
        {
            transform.GetOriginal().Describe(
                builder, displacementMap, transform, r.SpreadMethod, r.Channel, r.Signed);
        }
    }

    private static void DrawDisplacementMap(GeometrySession session, Brush.Resource map)
    {
        ImmediateCanvas canvas = session.OpenCanvas();
        float w = canvas.Density;
        using SKShader? shader =
            new BrushConstructor(new Rect(session.Bounds.Size), map, BlendMode.SrcOver, w, session.MaxWorkingScale)
                .CreateShader();
        using var paint = new SKPaint { Shader = shader };
        canvas.Canvas.DrawRect(new SKRect(0, 0, (float)session.Bounds.Width, (float)session.Bounds.Height), paint);
    }

}
