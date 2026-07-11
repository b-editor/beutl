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

        Point position = r.Position;
        var inflate = new Thickness(r.Sigma.Width * 3, r.Sigma.Height * 3);

        // The legacy InnerShadow is a two-draw canvas composite (blur + SrcOut shadow, then a DstATop/DstIn blend of
        // the source), not a Skia image-filter primitive — so it migrates to a GeometryNode that reproduces those
        // exact device-space draws (research D7's "else its own descriptor" clause). The output occupies the input
        // rect (forward identity), but the shadow draw samples a blurred (3σ), offset (Position) copy: a shadow texel
        // at output r comes from input at r − Position gathered over the 3σ blur radius. Backward must therefore claim
        // r ∪ (r − Position).Inflate(3σ) (the DropShadow backward pattern) — an identity backward under-claims and
        // crops an upstream pass below the shadow source.
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyGeometry(session, data),
            BoundsContract.Create(
                static rect => rect,
                rect => rect.Union(rect.Translate(-position).Inflate(inflate))),
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

        // Both draws anchor to the un-cropped OutputBounds origin, which for this identity-forward pass is the input
        // op's bounds origin. A downstream deflating pass can ROI-crop this pass so session.Bounds is an OFFSET
        // sub-rect of that; bridge the origin (like FlatShadow/Clipping) so content still registers to the actual
        // buffer. Zero when un-cropped (golden parity).
        float bridgeX = (float)(input.Bounds.X - session.Bounds.X) * w;
        float bridgeY = (float)(input.Bounds.Y - session.Bounds.Y) * w;
        bool bridged = bridgeX != 0 || bridgeY != 0;

        using (canvas.PushDeviceSpace())
        using (bridged ? canvas.PushTransform(Matrix.CreateTranslation(bridgeX, bridgeY)) : default)
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
