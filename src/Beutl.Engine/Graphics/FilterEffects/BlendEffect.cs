using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.BlendEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BlendEffect : FilterEffect
{
    public BlendEffect()
    {
        ScanProperties<BlendEffect>();
        Brush.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.BlendEffect_BlendMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcIn);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        BlendMode blendMode = r.BlendMode;

        // Brush kind is structural (A4): a solid-colour brush is a per-pixel blend of a constant colour, which is a
        // fusable ColorFilterNode; any other brush (gradient / image) paints geometry and stays a GeometryNode. The
        // colour and its opacity are parameters folded into the constant.
        if (r.Brush is SolidColorBrush.Resource solid)
        {
            Color color = solid.Color;
            float opacity = Math.Clamp(solid.Opacity / 100f, 0f, 1f);
            var effective = Color.FromArgb((byte)(color.A * opacity), color.R, color.G, color.B);
            builder.BlendMode(effective, blendMode);
            return;
        }

        Brush.Resource? brush = r.Brush;
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyBrushBlend(session, brush, blendMode),
            BoundsContract.Identity,
            structuralToken: nameof(BlendEffect)));
    }

    private static void ApplyBrushBlend(GeometrySession session, Brush.Resource? brush, BlendMode blendMode)
    {
        EffectInput input = session.Inputs[0];
        ImmediateCanvas canvas = session.OpenCanvas();
        float w = canvas.Density;
        Size size = session.Bounds.Size;

        var constructor = new BrushConstructor(new Rect(size), brush, blendMode, w, session.MaxWorkingScale);
        using var brushPaint = new SKPaint();
        constructor.ConfigurePaint(brushPaint);

        using (canvas.PushDeviceSpace())
        {
            input.Draw(canvas, default);
        }

        canvas.Canvas.DrawRect(SKRect.Create(size.ToSKSize()), brushPaint);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.Contains("Color"))
        {
            Color color = context.GetValue<Color>("Color");
            Brush.CurrentValue = new SolidColorBrush(color);
        }
    }
}
