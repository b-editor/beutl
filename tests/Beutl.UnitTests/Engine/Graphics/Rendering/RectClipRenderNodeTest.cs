using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectClipRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(rect, operation), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(default, operation), Is.True);
    }

    [Test]
    public void Update_ShouldNotMarkChanges_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        using var node = new RectClipRenderNode(rect, operation);
        node.HasChanges = false;

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(rect, operation), Is.False);
            Assert.That(node.HasChanges, Is.False);
        });
    }

    [Test]
    public void Update_ShouldMarkChanges_WhenPropertiesDoNotMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        using var node = new RectClipRenderNode(rect, operation);
        node.HasChanges = false;

        Assert.Multiple(() =>
        {
            Assert.That(node.Update(default, operation), Is.True);
            Assert.That(node.HasChanges, Is.True);
        });
    }

    [Test]
    public void UnchangedReRecording_ShouldAdmitTheClipScopeToTheCache()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        using var node = new RectClipRenderNode(rect, operation);

        for (int frame = 0; frame < RenderNodeCache.StableRequestCount; frame++)
        {
            node.Update(rect, operation);
            RenderNodeCacheHelper.BeginLifecycle(node).CompleteSuccessfully(advanceWarmup: true);
        }

        Assert.That(node.Cache.CanCapture, Is.True);
    }

    [Test]
    public void UnchangedClipScope_ShouldNotBlockAnAncestorCache()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        using var parent = new ContainerRenderNode();
        var node = new RectClipRenderNode(rect, operation);
        parent.AddChild(node);

        for (int frame = 0; frame < RenderNodeCache.StableRequestCount; frame++)
        {
            node.Update(rect, operation);
            RenderNodeCacheHelper.BeginLifecycle(parent).CompleteSuccessfully(advanceWarmup: true);
        }

        Assert.Multiple(() =>
        {
            Assert.That(parent.Cache.CanCapture, Is.True);
            Assert.That(node.Cache.CanCapture, Is.True);
        });
    }

    [Test]
    public void Measure_WithoutChild_ShouldReportNoFragments()
    {
        using var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.False);
    }

    [Test]
    public void Measure_WithChild_ShouldReportScopedFragment()
    {
        using var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(10, 20, 30, 40),
            Brushes.Resource.White,
            null));
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(10, 20, 30, 40)));
        });
    }

    [Test]
    public void Intersect_ClipsOutputBoundsAndHitTesting()
    {
        var clip = new Rect(20, 10, 30, 40);
        using var node = new RectClipRenderNode(clip, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        using var renderer = CreateRenderer(node);

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(clip));
            Assert.That(measurement.QueryBounds, Is.EqualTo(clip));
            Assert.That(renderer.HitTest(new Point(25, 25)), Is.True);
            Assert.That(renderer.HitTest(new Point(10, 25)), Is.False);
        });
    }

    [Test]
    public void ClipStateChanges_ReuseTheStructuralPlan()
    {
        using var cache = new StructuralPlanCache();
        using var node = new RectClipRenderNode(
            new Rect(10, 10, 40, 40),
            ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));

        using (Compile(cache, node))
        {
        }

        node.Update(new Rect(20, 20, 30, 30), ClipOperation.Difference);
        using CompiledRenderRequest compiled = Compile(cache, node);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 100, 100)));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    [Test]
    public void EquivalentIndependentlyConstructedScopeDefinitions_ReuseTheStructuralPlan()
    {
        using var cache = new StructuralPlanCache();
        using var node = new EquivalentScopeDefinitionNode();
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));

        using (Compile(cache, node))
        {
        }
        using (Compile(cache, node))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.DefinitionCreations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    [Test]
    public void DifferentScopeDefinitionContracts_RecompileTheStructuralPlan()
    {
        using var cache = new StructuralPlanCache();
        using var node = new ContractChangingScopeDefinitionNode();
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));

        using (Compile(cache, node))
        {
        }

        node.UseFullInputContract = true;
        using (Compile(cache, node))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.DefinitionCreations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Hits, Is.Zero);
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

    private static CompiledRenderRequest Compile(StructuralPlanCache cache, RenderNode node)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            cachePolicy: RenderCacheOptions.Disabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler(cache).Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private sealed class EquivalentScopeDefinitionNode : ContainerRenderNode
    {
        public int DefinitionCreations { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            DefinitionCreations++;
            TargetScopeDefinition<ScopeState> definition = TargetScopeDefinition<ScopeState>.Create(
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.Push())
                    {
                        session.ReplayInput();
                    }
                }),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                deviceGridMapping: RenderDeviceGridMapping.Preserved);
            context.PublishMappedInputs(
                definition.Call(default),
                static (current, input, call) => current.TargetScope(input, call));
        }

        private readonly record struct ScopeState;
    }

    private sealed class ContractChangingScopeDefinitionNode : ContainerRenderNode
    {
        public bool UseFullInputContract { get; set; }

        public int DefinitionCreations { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            DefinitionCreations++;
            RenderBoundsContract bounds = UseFullInputContract
                ? RenderBoundsContract.FullInput
                : RenderBoundsContract.Identity;
            TargetScopeDefinition<ScopeState> definition = TargetScopeDefinition<ScopeState>.Create(
                static (session, _) => session.Canvas.Use(canvas =>
                {
                    using (canvas.Push())
                    {
                        session.ReplayInput();
                    }
                }),
                bounds,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                deviceGridMapping: RenderDeviceGridMapping.Preserved);
            context.PublishMappedInputs(
                definition.Call(default),
                static (current, input, call) => current.TargetScope(input, call));
        }

        private readonly record struct ScopeState;
    }
}
