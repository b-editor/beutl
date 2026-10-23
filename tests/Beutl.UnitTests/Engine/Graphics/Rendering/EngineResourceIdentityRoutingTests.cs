using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics3D;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class EngineResourceIdentityRoutingTests
{
    private const int Iterations = 20000;
    private const int Rounds = 5;

    [Test]
    public void ADetachedResourceOfEveryRoutedSiteType_DerivesAnIdentityInsteadOfThrowing()
    {
        using var geometry = new EllipseGeometry.Resource();
        using var brush = new SolidColorBrush.Resource();
        using var pen = new Pen.Resource();
        using var emitter = new ParticleEmitter.Resource();
        using var effect = new ShakeEffect.Resource();
        using var scene = new Scene3D.Resource();
        EngineObject.Resource[] resources = [geometry, brush, pen, emitter, effect, scene];

        using (Assert.EnterMultipleScope())
        {
            foreach (EngineObject.Resource resource in resources)
            {
                Assert.That(resource.GetOriginal(), Is.Null,
                    $"{resource.GetType()} is detached, so it has no backing id");
                Guid first = Guid.Empty;
                Assert.DoesNotThrow(() => first = EngineResourceIdentity.Of(resource));
                Assert.That(EngineResourceIdentity.Of(resource), Is.EqualTo(first),
                    "the synthesized identity is held weakly against the resource and survives between reads");
                Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            }
        }
    }

    [Test]
    public void GeometryRenderNode_WithADetachedFill_RendersInsteadOfThrowing()
    {
        var geometry = new EllipseGeometry { Width = { Value = 40 }, Height = { Value = 30 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using var fill = new SolidColorBrush.Resource();
        using var node = new GeometryRenderNode(geometryResource, fill, null);

        Exception? failure = RecordAndCaptureFailure(node);

        Assert.That(failure, Is.Null, "recording a node with a detached fill must not throw");
    }

    [Test]
    public void GeometryRenderNode_WithADetachedGeometry_RecordsItsFragment()
    {
        using var geometry = new EllipseGeometry.Resource { Width = 40, Height = 30 };
        using var node = new GeometryRenderNode(geometry, null, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RecordAndCaptureFailure(node), Is.Null,
                "recording a node with a detached geometry must not throw");
            Assert.That(RecordedFragmentCount(node), Is.EqualTo(1));
        }
    }

    [Test]
    public void GeometryClipRenderNode_WithADetachedGeometry_RecordsItsScope()
    {
        using var geometry = new EllipseGeometry.Resource { Width = 40, Height = 30 };
        using var node = new GeometryClipRenderNode(geometry, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(new Rect(0, 0, 8, 8), null, null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RecordAndCaptureFailure(node), Is.Null,
                "recording a clip with a detached geometry must not throw");
            Assert.That(RecordedFragmentCount(node), Is.EqualTo(1));
        }
    }

    [Test]
    public void ParticleRenderNode_WithADetachedEmitter_RecordsNothingBecauseItHasNoAliveParticles()
    {
        using var emitter = new ParticleEmitter.Resource();
        using var node = new ParticleRenderNode(emitter);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(emitter.GetAliveParticles().Length, Is.Zero,
                "the simulator field is initialized inline but only Update ever simulates");
            Assert.That(RecordAndCaptureFailure(node), Is.Null);
            Assert.That(RecordedFragmentCount(node), Is.Zero);
        }
    }

    [Test]
    public void Scene3DRenderNode_WithADetachedScene_RecordsNothingBecauseItHasNoCamera()
    {
        using var scene = new Scene3D.Resource();
        using var node = new Scene3DRenderNode(scene);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scene.Camera, Is.Null);
            Assert.That(RecordAndCaptureFailure(node), Is.Null);
            Assert.That(RecordedFragmentCount(node), Is.Zero);
        }
    }


    [Test]
    public void AnAttachedResourceOfEveryRoutedSiteType_YieldsItsIdentityWithoutAllocating()
    {
        var geometry = new EllipseGeometry { Width = { Value = 40 }, Height = { Value = 30 } };
        var fill = new SolidColorBrush(Colors.Red);
        var pen = new Pen { Brush = { Value = Brushes.Black }, Thickness = { Value = 2 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using SolidColorBrush.Resource fillResource = fill.ToResource(CompositionContext.Default);
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        var emitter = new ParticleEmitter();
        using var emitterResource =
            (ParticleEmitter.Resource)emitter.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var effect = new ShakeEffect();
        using FilterEffect.Resource effectResource = effect.ToResource(CompositionContext.Default);
        var scene = new Scene3D();
        using var sceneResource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        EngineObject.Resource[] resources =
            [geometryResource, fillResource, penResource, emitterResource, effectResource, sceneResource];

        using (Assert.EnterMultipleScope())
        {
            foreach (EngineObject.Resource resource in resources)
            {
                Assert.That(Measure(() => _ = EngineResourceIdentity.Of(resource)), Is.Zero,
                    $"{resource.GetType()} must yield its identity without allocating");
            }
        }
    }

    private static long Measure(Action build)
    {
        for (int index = 0; index < 200; index++)
            build();

        long best = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Iterations; index++)
                build();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        return best;
    }

    private static Exception? RecordAndCaptureFailure(RenderNode node)
    {
        try
        {
            _ = RecordedFragmentCount(node);
            return null;
        }
        catch (Exception ex)
        {
            return Unwrap(ex);
        }
    }

    private static int RecordedFragmentCount(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        return new RenderRequestRecorder(request).Record(node).PublicationRoots.Count();
    }

    private static Exception Unwrap(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is { } inner)
            current = inner;
        return current;
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: new Rect(0, 0, 32, 32),
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner));
}
