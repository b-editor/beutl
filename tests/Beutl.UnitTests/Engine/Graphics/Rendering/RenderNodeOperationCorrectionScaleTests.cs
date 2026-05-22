using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderNodeOperationCorrectionScaleTests
{
    private static readonly Rect s_bounds = new(0, 0, 1920, 1080);

    [Test]
    public void CreateLambda_WithoutScale_ReportsIdentity()
    {
        using var op = RenderNodeOperation.CreateLambda(s_bounds, _ => { });
        Assert.That(op.CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    [Test]
    public void CreateLambda_DefaultRenderScaleArg_NormalizesToIdentity()
    {
        // `default(RenderScale)` is (0, 0); the factory must normalize it to Identity
        // so existing callers stay byte-identical without specifying the new parameter.
        using var op = RenderNodeOperation.CreateLambda(s_bounds, _ => { }, correctionScale: default);
        Assert.That(op.CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    [Test]
    public void CreateLambda_WithExplicitScale_HonoursIt()
    {
        var scale = new RenderScale(4f, 4f);
        using var op = RenderNodeOperation.CreateLambda(s_bounds, _ => { }, correctionScale: scale);
        Assert.That(op.CorrectionScale, Is.EqualTo(scale));
    }

    [Test]
    public void CreateLambda_WithNonUniformScale_HonoursPerAxis()
    {
        var scale = new RenderScale(4f, 2f);
        using var op = RenderNodeOperation.CreateLambda(s_bounds, _ => { }, correctionScale: scale);
        Assert.That(op.CorrectionScale.ScaleX, Is.EqualTo(4f));
        Assert.That(op.CorrectionScale.ScaleY, Is.EqualTo(2f));
    }

    [Test]
    public void CreateDecorator_InheritsChildCorrectionScale()
    {
        var scale = new RenderScale(4f, 4f);
        var child = RenderNodeOperation.CreateLambda(s_bounds, _ => { }, correctionScale: scale);

        using var wrapped = RenderNodeOperation.CreateDecorator(child, _ => { });

        Assert.That(wrapped.CorrectionScale, Is.EqualTo(scale));
    }

    [Test]
    public void CreateDecorator_IdentityChild_StaysIdentity()
    {
        var child = RenderNodeOperation.CreateLambda(s_bounds, _ => { });

        using var wrapped = RenderNodeOperation.CreateDecorator(child, _ => { });

        Assert.That(wrapped.CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    [Test]
    public void DefaultVirtual_ReturnsIdentity()
    {
        var op = new IdentityFakeOperation();
        Assert.That(op.CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    private sealed class IdentityFakeOperation : RenderNodeOperation
    {
        public override Rect Bounds => default;

        public override void Render(ImmediateCanvas canvas)
        {
        }

        public override bool HitTest(Point point) => false;
    }
}
