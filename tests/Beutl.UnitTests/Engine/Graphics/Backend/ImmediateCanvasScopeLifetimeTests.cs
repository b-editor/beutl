using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// The ambient hook scopes ImmediateCanvas hands back must close in LIFO order: saving and restoring a value would let
// an outer scope closed early re-install its ended hook, which every canvas built afterwards inherits. Each case runs
// inside one InvokeOnRenderThread call, so a value an out-of-order dispose leaves pinned reaches no other test.
[NonParallelizable]
[TestFixture]
public class ImmediateCanvasScopeLifetimeTests
{
    [Test]
    public void PushDrawableBrushMaterializer_DisposedOutOfOrder_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
            DrawableBrushMaterializer outerHook = (_, _, _) => null;
            DrawableBrushMaterializer innerHook = (_, _, _) => null;

            IDisposable outer = canvas.PushDrawableBrushMaterializer(outerHook);
            IDisposable inner = canvas.PushDrawableBrushMaterializer(innerHook);

            Assert.Throws<InvalidOperationException>(outer.Dispose);
            Assert.Multiple(() =>
            {
                Assert.That(canvas.DrawableBrushMaterializer, Is.SameAs(innerHook),
                    "the live inner hook must survive an outer scope closed early");
                Assert.That(AmbientMaterializerOfANewCanvas(), Is.SameAs(innerHook),
                    "a canvas built afterwards must not inherit a hook whose scope has ended");
            });

            inner.Dispose();
        });
    }

    [Test]
    public void PushDrawableBrushMaterializer_SameHookNestedTwice_StillDetectsOutOfOrderDisposal()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);

            // Two scopes may install the same delegate, so the installed value cannot identify the
            // innermost scope. Only the scope object itself can.
            DrawableBrushMaterializer hook = (_, _, _) => null;

            IDisposable outer = canvas.PushDrawableBrushMaterializer(hook);
            IDisposable inner = canvas.PushDrawableBrushMaterializer(hook);

            Assert.Throws<InvalidOperationException>(outer.Dispose);
            Assert.DoesNotThrow(inner.Dispose, "the innermost scope is still the one that may close");
        });
    }

    [Test]
    public void PushDrawableBrushMaterializer_DisposedInLifoOrder_RestoresTheOuterHook()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
            DrawableBrushMaterializer outerHook = (_, _, _) => null;
            DrawableBrushMaterializer innerHook = (_, _, _) => null;

            IDisposable outer = canvas.PushDrawableBrushMaterializer(outerHook);
            IDisposable inner = canvas.PushDrawableBrushMaterializer(innerHook);

            inner.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(canvas.DrawableBrushMaterializer, Is.SameAs(outerHook));
                Assert.That(AmbientMaterializerOfANewCanvas(), Is.SameAs(outerHook),
                    "a canvas created inside the outer scope adopts the outer hook");
            });

            outer.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(canvas.DrawableBrushMaterializer, Is.Null);
                Assert.That(AmbientMaterializerOfANewCanvas(), Is.Null);
            });

            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(outer.Dispose, "closing a scope twice is a no-op");
                Assert.DoesNotThrow(inner.Dispose);
            });
        });
    }

    [Test]
    public void PushRenderTargetLeaseSession_DisposedOutOfOrder_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
            using var outerRegistry = new RenderTargetPool(factory: null);
            using var innerRegistry = new RenderTargetPool(factory: null);
            using RenderTargetLeaseSession outerSession = outerRegistry.BeginSession(RenderIntent.Preview);
            using RenderTargetLeaseSession innerSession = innerRegistry.BeginSession(RenderIntent.Preview);

            IDisposable outer = canvas.PushRenderTargetLeaseSession(outerSession);
            IDisposable inner = canvas.PushRenderTargetLeaseSession(innerSession);

            Assert.Throws<InvalidOperationException>(outer.Dispose);
            Assert.That(canvas.RenderTargetLeaseSession, Is.SameAs(innerSession),
                "the live inner session must survive an outer scope closed early");

            inner.Dispose();
        });
    }

    [Test]
    public void PushRenderTargetLeaseSession_DisposedInLifoOrder_RestoresTheOuterSession()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
            using var outerRegistry = new RenderTargetPool(factory: null);
            using var innerRegistry = new RenderTargetPool(factory: null);
            using RenderTargetLeaseSession outerSession = outerRegistry.BeginSession(RenderIntent.Preview);
            using RenderTargetLeaseSession innerSession = innerRegistry.BeginSession(RenderIntent.Preview);

            IDisposable outer = canvas.PushRenderTargetLeaseSession(outerSession);
            IDisposable inner = canvas.PushRenderTargetLeaseSession(innerSession);

            inner.Dispose();
            Assert.That(canvas.RenderTargetLeaseSession, Is.SameAs(outerSession));

            outer.Dispose();
            Assert.That(canvas.RenderTargetLeaseSession, Is.Null);
        });
    }

    /// <summary>The ambient hook a freshly constructed canvas inherits.</summary>
    private static DrawableBrushMaterializer? AmbientMaterializerOfANewCanvas()
    {
        using var target = RenderTarget.Create(16, 16)!;
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
        return canvas.DrawableBrushMaterializer;
    }
}
