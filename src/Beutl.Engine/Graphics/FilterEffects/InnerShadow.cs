using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.InnerShadow), ResourceType = typeof(GraphicsStrings))]
public partial class InnerShadow : FilterEffect
{
    public InnerShadow()
    {
        ScanProperties<InnerShadow>();
    }

    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> Position { get; } = Property.CreateAnimatable(new Point());

    [Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(GraphicsStrings.ShadowOnly), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var data = (r.Position, r.Sigma, r.Color,
            BlendMode: r.ShadowOnly ? BlendMode.DstIn : BlendMode.DstATop);

        // The legacy InnerShadow is a two-draw canvas composite (blur + SrcOut shadow, then a DstATop/DstIn blend of
        // the source), not a Skia image-filter primitive — so it migrates to a GeometryNode that reproduces those
        // exact device-space draws (research D7's "else its own descriptor" clause). Bounds are identity: the output
        // occupies the input rect and every sampled texel comes from it, so the backward map is identity too.
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyGeometry(session, data),
            BoundsContract.Identity,
            structuralToken: r.ShadowOnly ? "InnerShadowOnly" : "InnerShadow"));
    }

    private static void ApplyGeometry(
        GeometrySession session, (Point Position, Size Sigma, Color Color, BlendMode BlendMode) data)
    {
        EffectInput input = session.Inputs[0];
        ImmediateCanvas canvas = session.OpenCanvas();
        // Blur radius and the shadow offset live in the output buffer's device space, so scale by its density.
        float w = canvas.Density;

        using var blur = SKImageFilter.CreateBlur(data.Sigma.Width * w, data.Sigma.Height * w);
        using var blend = SKColorFilter.CreateBlendMode(data.Color.ToSKColor(), SKBlendMode.SrcOut);
        using var filter = SKImageFilter.CreateColorFilter(blend, blur);
        using var paint = new SKPaint { ImageFilter = filter };

        using (canvas.PushDeviceSpace())
        {
            using (canvas.PushPaint(paint))
            {
                input.Draw(canvas, new Point(data.Position.X * w, data.Position.Y * w));
            }

            using (canvas.PushBlendMode(data.BlendMode))
            {
                input.Draw(canvas, default);
            }
        }
    }
}
