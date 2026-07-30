using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Blur), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Blur : FilterEffect
{
    public Blur()
    {
        ScanProperties<Blur>();
    }

    [Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Blur(r.Sigma);
    }

    public partial class Resource
    {
        public override FilterEffectRenderNode CreateRenderNode()
        {
            return new BlurRenderNode(this);
        }
    }

    private sealed class BlurRenderNode(Resource effect) : FilterEffectRenderNode(effect)
    {
        protected override RenderScaleContract? GetWorkingScaleContract()
        {
            Size sigma = FilterEffectContext.NormalizeGaussianSigma(
                ((Resource)FilterEffect!.Value.Resource).Sigma);
            return new GaussianBlurWorkingScaleResolver(MathF.Max(sigma.Width, sigma.Height))
                .CreateContract();
        }
    }
}
