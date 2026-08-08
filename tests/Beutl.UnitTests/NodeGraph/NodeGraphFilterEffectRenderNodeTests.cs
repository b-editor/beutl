using System.Linq;
using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.NodeGraph;

// NodeGraphFilterEffectRenderNode.Process forwards OutputScale / MaxWorkingScale through request-local
// graph recording. These tests assert those scales reach the effect inside the graph.
[TestFixture]
public class NodeGraphFilterEffectRenderNodeTests
{
    // Graph: FilterEffectInputNode -> FilterEffectNode<ScaleProbeEffect> -> OutputNode.
    private static NodeGraphFilterEffect.Resource BuildGraphResource()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;

        var inputNode = new FilterEffectInputNode();
        var probeNode = new FilterEffectNode<ScaleProbeEffect>();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(probeNode);
        model.Nodes.Add(outputNode);

        // The render-chain ports are not exposed publicly: Items[0] = Output, Items[1] = list Input
        // (per-property value inputs follow at Items[2+]). Reach them by index.
        var probeChainInput = (IInputPort)probeNode.Items[1];
        var probeChainOutput = (IOutputPort)probeNode.Items[0];
        model.Connect(probeChainInput, inputNode.Output);
        model.Connect(outputNode.InputPort, probeChainOutput);

        return effect.ToResource(CompositionContext.Default);
    }

    // An At(1) source lets OutputScale drive the working scale: w = max(s_out, 1).
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    [TestCase(4.0f, 4.0f)]
    public void Process_ForwardsOutputScale_IntoGraphOutputSubtree(float outputScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            resource.CreateRenderNode(),
            EffectiveScale.At(1),
            outputScale);

        Assert.That(measurement.HasFragments, Is.True, "the graph dropped the input fragment");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded OutputScale did not drive the working scale inside the graph");
    }

    // An At(4) source pushes supply above s_out, so only the forwarded MaxWorkingScale can cap it.
    [TestCase(float.PositiveInfinity, 4.0f)]
    [TestCase(2.0f, 2.0f)]
    public void Process_ForwardsMaxWorkingScale_IntoGraphOutputSubtree(float maxWorkingScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            resource.CreateRenderNode(),
            EffectiveScale.At(4),
            outputScale: 1,
            maxWorkingScale);

        Assert.That(measurement.HasFragments, Is.True, "the graph dropped the input fragment");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded MaxWorkingScale did not cap the working scale inside the graph");
    }

    [Test]
    public void ToResource_CapturesProxyPreferencesFromCompositionContext()
    {
        var effect = new NodeGraphFilterEffect();
        var context = new CompositionContext(TimeSpan.Zero)
        {
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Eighth,
            DisableResourceShare = true,
        };

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(context);

        // The render node replays the graph with a fresh context; without these captured flags a
        // VideoSourceNode inside the graph evaluates with PreferProxy=false and loses export-time
        // reader isolation (DisableResourceShare=false).
        Assert.Multiple(() =>
        {
            Assert.That(resource.PreferProxy, Is.True);
            Assert.That(resource.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Eighth));
            Assert.That(resource.DisableResourceShare, Is.True);
        });
    }

    // The render node leaves ChildNodes empty, so neither revalidation nor cache recursion can see the graph
    // output subtree. That is sound only while the node itself never reaches the cache-admission threshold.
    [Test]
    public void RepeatedBuilds_NeverAdmitTheGraphRenderNodeToTheCache()
    {
        var effect = new NodeGraphFilterEffect();
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.CreateRenderNode();

        for (int i = 0; i < RenderNodeCache.Count + 1; i++)
        {
            bool updateOnly = false;
            resource.Update(effect, CompositionContext.Default, ref updateOnly);
            node.Update(resource);
            node.Cache.IncrementRenderCount();

            Assert.That(node.Cache.CanCache(), Is.False,
                "a build left the graph render node cacheable although its subtree is invisible to the cache pass");
        }
    }

    [Test]
    public void ToResource_DefaultContext_LeavesProxyPreferencesOff()
    {
        var effect = new NodeGraphFilterEffect();

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);

        Assert.That(resource.PreferProxy, Is.False);
    }

    [Test]
    public void Process_WhenGraphResourceIsDisabled_PassesThroughWithoutEvaluatingGraph()
    {
        var bounds = new Rect(2, 3, 18, 12);
        var graph = BuildUtilityGraph(connectPreview: true);
        using NodeGraphFilterEffect.Resource resource = graph.Resource;
        resource.IsEnabled = false;
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(graph.Preview);
        monitor.IsEnabled = true;
        using Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        monitor.Value = previous;
        var source = new CountingOpaqueSourceRenderNode(bounds);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(bounds));
            Assert.That(source.ExecutionCount, Is.EqualTo(1));
            Assert.That(graph.Shared.EvaluationCount, Is.Zero,
                "A disabled NodeGraphFilterEffect resource must not evaluate its snapshot.");
            Assert.That(graph.Shared.ProcessCount, Is.Zero);
            Assert.That(monitor.Value, Is.SameAs(previous));
            Assert.That(previous.Value.IsDisposed, Is.False);
        });
    }

    [Test]
    public void MeasureAndPreview_UseBoundInputAndShareOneRecording()
    {
        var bounds = new Rect(7, 11, 48, 32);
        var graph = BuildUtilityGraph(connectPreview: true);
        using NodeGraphFilterEffect.Resource resource = graph.Resource;
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(graph.Preview);
        monitor.IsEnabled = true;
        Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        monitor.Value = previous;

        var source = new CountingOpaqueSourceRenderNode(bounds);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.QueryBounds, Is.EqualTo(bounds));
            Assert.That(graph.MeasureCapture.Value, Is.EqualTo(bounds),
                "Measure must read the request-bound FilterEffect input metadata.");
            Assert.That(graph.Shared.ProcessCount, Is.EqualTo(1),
                "Measure, Preview, and Output must share one identity-cached subtree recording.");
            Assert.That(source.ExecutionCount, Is.Zero,
                "Preview readback must not execute while the request is only being recorded/measured.");
            Assert.That(monitor.Value, Is.SameAs(previous));
            Assert.That(previous.Value, Is.Not.Null);
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Ref<Bitmap>? replacement = monitor.Value;
        Assert.Multiple(() =>
        {
            Assert.That(graph.Shared.ProcessCount, Is.EqualTo(2),
                "The shared subtree should be recorded once in each request.");
            Assert.That(source.ExecutionCount, Is.EqualTo(1),
                "Fan-out to Output and Preview must not duplicate the deferred source side effect.");
            Assert.That(replacement, Is.Not.Null.And.Not.SameAs(previous));
            Assert.That(replacement!.Value.Width, Is.EqualTo(48));
            Assert.That(replacement.Value.Height, Is.EqualTo(32));
            Assert.That(previous.Value, Is.Null,
                "Replacing the monitor value must release the previous Ref ownership.");
        });

        replacement?.Dispose();
    }

    [Test]
    public void Preview_WithNullInput_DefersClearUntilExecution()
    {
        var graph = BuildUtilityGraph(connectPreview: false);
        using NodeGraphFilterEffect.Resource resource = graph.Resource;
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(graph.Preview);
        monitor.IsEnabled = true;
        Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        monitor.Value = previous;

        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 16, 12));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        renderer.Measure();
        Assert.That(monitor.Value, Is.SameAs(previous),
            "An empty preview must not mutate its monitor during recording.");

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Value, Is.Null);
            Assert.That(previous.Value, Is.Null,
                "The deferred empty-preview command must release the previous monitor value.");
        });
    }

    [Test]
    public void Preview_ZeroOrOneInputThatProducesNoValue_ClearsDuringExecution()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        using var emptyRenderNode = new EmptyZeroOrOneRenderNode(new Rect(0, 0, 16, 12));
        var emptyNode = new FixedRenderNodeGraphNode(emptyRenderNode);
        var previewNode = new PreviewNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(emptyNode);
        model.Nodes.Add(previewNode);
        model.Nodes.Add(outputNode);
        model.Connect(previewNode.Input, emptyNode.Output);
        model.Connect(outputNode.InputPort, emptyNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(previewNode);
        monitor.IsEnabled = true;
        Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        monitor.Value = previous;
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 16, 12));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(emptyRenderNode.ExecutionCount, Is.EqualTo(1));
            Assert.That(source.ExecutionCount, Is.Zero,
                "The optional graph source does not consume the FilterEffect input.");
            Assert.That(monitor.Value, Is.Null,
                "A readback command whose optional input produced no runtime value must clear the preview.");
            Assert.That(previous.Value, Is.Null);
        });
    }

    [Test]
    public void Preview_WhenDisabled_LeavesExistingValueUntouched()
    {
        var graph = BuildUtilityGraph(connectPreview: true);
        using NodeGraphFilterEffect.Resource resource = graph.Resource;
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(graph.Preview);
        monitor.IsEnabled = false;
        Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        monitor.Value = previous;

        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 16, 12));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Value, Is.SameAs(previous));
            Assert.That(previous.Value, Is.Not.Null);
        });

        previous.Dispose();
    }

    [Test]
    public void Preview_WhenContentChangedThrows_RestoresPreviousOwnership()
    {
        var graph = BuildUtilityGraph(connectPreview: true);
        using NodeGraphFilterEffect.Resource resource = graph.Resource;
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(graph.Preview);
        monitor.IsEnabled = true;
        using Ref<Bitmap> previous = Ref<Bitmap>.Create(new Bitmap(1, 1));
        Bitmap previousBitmap = previous.Value;
        monitor.Value = previous;
        Ref<Bitmap>? attempted = null;
        Bitmap? attemptedBitmap = null;
        int notifications = 0;
        EventHandler handler = (_, _) =>
        {
            notifications++;
            if (notifications == 1)
            {
                attempted = monitor.Value;
                attemptedBitmap = attempted?.Value;
                throw new InvalidOperationException("preview monitor notification failed");
            }
        };
        monitor.ContentChanged += handler;
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 16, 12));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        InvalidOperationException? failure;
        try
        {
            failure = Assert.Throws<InvalidOperationException>(() =>
            {
                using RenderNodeRasterization rasterization = renderer.Rasterize();
            });
        }
        finally
        {
            monitor.ContentChanged -= handler;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("preview monitor notification failed"));
            Assert.That(notifications, Is.EqualTo(2),
                "The failed assignment must issue one restoration notification.");
            Assert.That(monitor.Value, Is.SameAs(previous));
            Assert.That(previous.Value, Is.SameAs(previousBitmap));
            Assert.That(previousBitmap.IsDisposed, Is.False);
            Assert.That(attempted, Is.Not.Null.And.Not.SameAs(previous));
            Assert.That(attempted!.Value, Is.Null,
                "The failed replacement Ref must be released exactly once by its caller.");
            Assert.That(attemptedBitmap, Is.Not.Null);
            Assert.That(attemptedBitmap!.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Preview_MixedMultipleOutputs_AreCompositedBeforeDeferredReadback()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var mixedNode = new MixedPreviewGraphNode();
        var previewNode = new PreviewNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(mixedNode);
        model.Nodes.Add(previewNode);
        model.Nodes.Add(outputNode);
        model.Connect(mixedNode.Input, inputNode.Output);
        model.Connect(previewNode.Input, mixedNode.Output);
        model.Connect(outputNode.InputPort, mixedNode.Output);

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(previewNode);
        monitor.IsEnabled = true;
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 20, 10));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Value, Is.Not.Null);
            Assert.That(monitor.Value!.Value.Width, Is.EqualTo(20));
            Assert.That(monitor.Value.Value.Height, Is.EqualTo(10));
            Assert.That(source.ExecutionCount, Is.EqualTo(1),
                "The normalized preview layer must be reused by the later graph output.");
            Assert.That(mixedNode.CommandExecutionCount, Is.EqualTo(1),
                "A mixed non-value output must stay ordered inside the normalized preview layer.");
        });

        monitor.Value?.Dispose();
    }

    [Test]
    public void DuplicateOutputRoots_NormalizeSharedNonValueSubtreeBeforePublication()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var mixedNode = new MixedPreviewGraphNode();
        var firstOutput = new OutputNode();
        var secondOutput = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(mixedNode);
        model.Nodes.Add(firstOutput);
        model.Nodes.Add(secondOutput);
        model.Connect(mixedNode.Input, inputNode.Output);
        model.Connect(firstOutput.InputPort, mixedNode.Output);
        model.Connect(secondOutput.InputPort, mixedNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 20, 10));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(source.ExecutionCount, Is.EqualTo(1));
            Assert.That(mixedNode.CommandExecutionCount, Is.EqualTo(1));
        });
    }

    // A NodeGraphFilterEffect on a DrawableGroup nests PushFilterEffect around PushLayer, so the graph's
    // bound inputs are a TargetLayerScope(Full) whose recording metadata stays symbolic.
    [Test]
    public void Preview_WithSymbolicInputBounds_RendersTheOwningTargetDomain()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var previewNode = new PreviewNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(previewNode);
        model.Nodes.Add(outputNode);
        model.Connect(previewNode.Input, inputNode.Output);
        model.Connect(outputNode.InputPort, inputNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(previewNode);
        monitor.IsEnabled = true;

        var source = new CountingOpaqueSourceRenderNode(new Rect(3, 5, 24, 18));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            source,
            new LayerRenderNode(default),
            resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = new Rect(0, 0, 64, 48),
                CacheOptions = RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Value, Is.Not.Null,
                "A symbolic-bounds subtree must not be previewed as an empty subtree.");
            Assert.That(monitor.Value!.Value.Width, Is.EqualTo(64));
            Assert.That(monitor.Value.Value.Height, Is.EqualTo(48));
        });

        monitor.Value?.Dispose();
    }

    // The recording node observes its own local space, which every enclosing target scope separates from
    // root space, so a normalizing layer sized from the root request rect clips local content away.
    // The child paints at local (-40,-40,80,80) and the enclosing translate maps it to root (60,60,80,80).
    [TestCase(true)]
    [TestCase(false)]
    public void SymbolicNormalization_UnderATranslatedEnclosingScope_KeepsContentOutsideTheRootDomainRect(
        bool preview)
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(outputNode);
        model.Connect(outputNode.InputPort, inputNode.Output);
        NodeMonitor<Ref<Bitmap>?>? monitor = null;
        if (preview)
        {
            var previewNode = new PreviewNode();
            model.Nodes.Add(previewNode);
            model.Connect(previewNode.Input, inputNode.Output);
            monitor = GetPreviewMonitor(previewNode);
            monitor.IsEnabled = true;
        }
        else
        {
            var secondOutput = new OutputNode();
            model.Nodes.Add(secondOutput);
            model.Connect(secondOutput.InputPort, inputNode.Output);
        }

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var content = new Rect(60, 60, 80, 80);
        var source = new CountingOpaqueSourceRenderNode(new Rect(-40, -40, 80, 80));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            source,
            new LayerRenderNode(default),
            resource.CreateRenderNode(),
            new TransformRenderNode(Matrix.CreateTranslation(100, 100), TransformOperator.Prepend));
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = new Rect(0, 0, 200, 200),
                CacheOptions = RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The translated graph subtree produced no bitmap.");
        Assert.That(rasterization.Bounds.Contains(content), Is.True,
            "Normalizing the graph subtree must not shrink the output extent to the root domain rect "
            + "reinterpreted in the recording node's local space.");

        var sample = bitmap.SKBitmap.GetPixel(
            (int)(content.Center.X - rasterization.Bounds.X),
            (int)(content.Center.Y - rasterization.Bounds.Y));

        Assert.That(sample.Alpha, Is.EqualTo(byte.MaxValue),
            "Content whose local coordinates fall outside the root domain rect must still be composited.");

        monitor?.Value?.Dispose();
    }

    [Test]
    public void DuplicateOutputRoots_WithSymbolicBounds_NormalizeAgainstTheOwningTargetDomain()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var firstOutput = new OutputNode();
        var secondOutput = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(firstOutput);
        model.Nodes.Add(secondOutput);
        model.Connect(firstOutput.InputPort, inputNode.Output);
        model.Connect(secondOutput.InputPort, inputNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);

        var source = new CountingOpaqueSourceRenderNode(new Rect(3, 5, 24, 18));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            source,
            new LayerRenderNode(default),
            resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = new Rect(0, 0, 64, 48),
                CacheOptions = RenderCacheOptions.Disabled,
            },
        });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bitmap, Is.Not.Null,
                "Two OutputNodes on one symbolic producer must still render.");
            Assert.That(source.ExecutionCount, Is.EqualTo(1));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SymbolicInputBounds_WithoutARequestTargetDomain_FailWithAnActionableMessage(bool preview)
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(outputNode);
        model.Connect(outputNode.InputPort, inputNode.Output);
        if (preview)
        {
            var previewNode = new PreviewNode();
            model.Nodes.Add(previewNode);
            model.Connect(previewNode.Input, inputNode.Output);
            GetPreviewMonitor(previewNode).IsEnabled = true;
        }
        else
        {
            var secondOutput = new OutputNode();
            model.Nodes.Add(secondOutput);
            model.Connect(secondOutput.InputPort, inputNode.Output);
        }

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var source = new CountingOpaqueSourceRenderNode(new Rect(3, 5, 24, 18));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            source,
            new LayerRenderNode(default),
            resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = RenderCacheOptions.Disabled,
            },
        });

        Assert.That(
            () => renderer.Measure(),
            Throws.TypeOf<RenderTargetDomainRequiredException>()
                .And.Message.Contains("requires a finite TargetDomain"));
    }

    [Test]
    public void Measure_WithSymbolicInputBounds_ReportsTheRecordedQueryBounds()
    {
        var bounds = new Rect(3, 5, 24, 18);
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var measureNode = new MeasureNode();
        var captureNode = new MeasureCaptureNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(measureNode);
        model.Nodes.Add(captureNode);
        model.Nodes.Add(outputNode);
        model.Connect(measureNode.Input, inputNode.Output);
        model.Connect(captureNode.X, measureNode.X);
        model.Connect(captureNode.Y, measureNode.Y);
        model.Connect(captureNode.Width, measureNode.Width);
        model.Connect(captureNode.Height, measureNode.Height);
        model.Connect(outputNode.InputPort, inputNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);

        var source = new CountingOpaqueSourceRenderNode(bounds);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            source,
            new LayerRenderNode(default),
            resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = new Rect(0, 0, 64, 48),
                CacheOptions = RenderCacheOptions.Disabled,
            },
        });

        renderer.Measure();

        Assert.That(captureNode.Value, Is.EqualTo(bounds),
            "MeasureNode must report the recorded query bounds of a symbolic-bounds subtree, not zero.");
    }

    [Test]
    public void SharedRenderNodeCycle_IsRejectedByBoundGraphRecorder()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var cycle = new ContainerRenderNode();
        cycle.AddChild(cycle);
        var cycleNode = new FixedRenderNodeGraphNode(cycle);
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(cycleNode);
        model.Nodes.Add(outputNode);
        model.Connect(outputNode.InputPort, cycleNode.Output);

        try
        {
            using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
            var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 8, 8));
            using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
            using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

            Assert.That(
                () => renderer.Measure(),
                Throws.InvalidOperationException.And.Message.Contains("node-graph render cycle"));
        }
        finally
        {
            cycle.RemoveChild(cycle);
            cycle.Dispose();
        }
    }

    [Test]
    public void PreviewCommands_ReuseStructuralPlanWithStableDistinctRuntimeIdentities()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var firstPreview = new PreviewNode();
        var secondPreview = new PreviewNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(firstPreview);
        model.Nodes.Add(secondPreview);
        model.Nodes.Add(outputNode);
        model.Connect(firstPreview.Input, inputNode.Output);
        model.Connect(secondPreview.Input, inputNode.Output);
        model.Connect(outputNode.InputPort, inputNode.Output);
        GetPreviewMonitor(firstPreview).IsEnabled = true;
        GetPreviewMonitor(secondPreview).IsEnabled = true;
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var bounds = new Rect(0, 0, 18, 12);
        var source = new CountingOpaqueSourceRenderNode(bounds);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());

        TargetCommandDescription[] firstCommands = RecordTargetCommands(pipeline, bounds);
        TargetCommandDescription[] secondCommands = RecordTargetCommands(pipeline, bounds);
        object[] firstRuntimeKeys = firstCommands
            .Select(static command => command.RuntimeIdentity!.Value.Key)
            .ToArray();
        object[] secondRuntimeKeys = secondCommands
            .Select(static command => command.RuntimeIdentity!.Value.Key)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstCommands, Has.Length.EqualTo(2));
            Assert.That(secondCommands, Has.Length.EqualTo(2));
            Assert.That(firstRuntimeKeys[0], Is.Not.EqualTo(firstRuntimeKeys[1]));
            Assert.That(secondRuntimeKeys, Is.EqualTo(firstRuntimeKeys),
                "Each PreviewNode runtime identity must remain stable across requests.");
            foreach (TargetCommandDescription command in firstCommands)
            {
                Assert.That(command.Resources, Has.Count.EqualTo(1),
                    "The deferred callback reaches only the replacement sink, and reaches it as a declared "
                    + "resource rather than a capture, so it can retain no transaction handle.");
            }
        });

        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });
        using (RenderNodeRasterization first = renderer.Rasterize())
        {
        }
        using (RenderNodeRasterization second = renderer.Rasterize())
        {
        }

        StructuralPlanCacheStatistics statistics = renderer.StructuralPlanCacheStatistics;
        Assert.Multiple(() =>
        {
            Assert.That(statistics.Compilations, Is.EqualTo(1));
            Assert.That(statistics.Misses, Is.EqualTo(1));
            Assert.That(statistics.Hits, Is.EqualTo(1));
        });

        GetPreviewMonitor(firstPreview).Value?.Dispose();
        GetPreviewMonitor(secondPreview).Value?.Dispose();
    }

    [Test]
    public void Measure_WithoutFilterBinding_PreservesStandaloneGraphBehavior()
    {
        var bounds = new Rect(3, 5, 24, 18);
        var source = new CountingOpaqueSourceRenderNode(bounds);
        using var renderNode = new LayerRenderNode(default);
        renderNode.AddChild(source);
        var model = new GraphModel();
        var sourceNode = new FixedRenderNodeGraphNode(renderNode);
        var measureNode = new MeasureNode();
        var captureNode = new MeasureCaptureNode();
        model.Nodes.Add(sourceNode);
        model.Nodes.Add(measureNode);
        model.Nodes.Add(captureNode);
        model.Connect(measureNode.Input, sourceNode.Output);
        model.Connect(captureNode.X, measureNode.X);
        model.Connect(captureNode.Y, measureNode.Y);
        model.Connect(captureNode.Width, measureNode.Width);
        model.Connect(captureNode.Height, measureNode.Height);
        using var snapshot = new GraphSnapshot();

        var context = new CompositionContext(TimeSpan.Zero)
        {
            TargetDomain = new Rect(0, 0, 64, 48),
        };
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);

        Assert.Multiple(() =>
        {
            Assert.That(captureNode.Value, Is.EqualTo(bounds));
            Assert.That(source.ExecutionCount, Is.Zero,
                "Standalone Measure should resolve a Full target layer without executing deferred work.");
        });
    }

    [Test]
    public void Preview_WithoutFilterBinding_RendersTheCompositionDomainForFullScopes()
    {
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 14, 9));
        using var renderNode = new LayerRenderNode(default);
        renderNode.AddChild(source);
        var model = new GraphModel();
        var sourceNode = new FixedRenderNodeGraphNode(renderNode);
        var previewNode = new PreviewNode();
        model.Nodes.Add(sourceNode);
        model.Nodes.Add(previewNode);
        model.Connect(previewNode.Input, sourceNode.Output);
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(previewNode);
        monitor.IsEnabled = true;
        using var snapshot = new GraphSnapshot();

        var context = new CompositionContext(TimeSpan.Zero)
        {
            TargetDomain = new Rect(0, 0, 64, 48),
        };
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);

        Assert.Multiple(() =>
        {
            Assert.That(source.ExecutionCount, Is.EqualTo(1));
            Assert.That(monitor.Value, Is.Not.Null);
            // A Full isolation scope makes the composition domain the root output extent
            // (RootOutputExtent covers conservative final writes; only Measure/HitTest
            // report the tight query bounds).
            Assert.That(monitor.Value!.Value.Width, Is.EqualTo(64));
            Assert.That(monitor.Value.Value.Height, Is.EqualTo(48));
        });

        monitor.Value?.Dispose();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StandaloneUtility_DoesNotRetryAnUnrelatedInvalidOperationException(bool preview)
    {
        using var renderNode = new ThrowingRenderNode();
        var model = new GraphModel();
        var sourceNode = new FixedRenderNodeGraphNode(renderNode);
        model.Nodes.Add(sourceNode);
        if (preview)
        {
            var previewNode = new PreviewNode();
            model.Nodes.Add(previewNode);
            model.Connect(previewNode.Input, sourceNode.Output);
            GetPreviewMonitor(previewNode).IsEnabled = true;
        }
        else
        {
            var measureNode = new MeasureNode();
            model.Nodes.Add(measureNode);
            model.Connect(measureNode.Input, sourceNode.Output);
        }

        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(TimeSpan.Zero)
        {
            TargetDomain = new Rect(0, 0, 64, 48),
        };
        snapshot.Build(model, context);

        Assert.That(
            () => snapshot.Evaluate(CompositionTarget.Graphics, context),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(ThrowingRenderNode.Message));
        Assert.That(renderNode.ProcessCount, Is.EqualTo(1),
            "An unrelated InvalidOperationException must propagate without a target-domain retry.");
    }

    [Test]
    public void SharedNonValueSubtree_ThrowsAtSecondNodeGraphConsumer()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var sharedNode = new SharedNonValueSubtreeGraphNode();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(sharedNode);
        model.Nodes.Add(outputNode);
        model.Connect(outputNode.InputPort, sharedNode.Output);
        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var source = new CountingOpaqueSourceRenderNode(new Rect(0, 0, 8, 8));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(source, resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        Assert.That(
            () => renderer.Measure(),
            Throws.InvalidOperationException.And.Message.Contains("used by more than one consumer"));
    }

    private static UtilityGraph BuildUtilityGraph(bool connectPreview)
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var shared = new CountingPassThroughGraphNode();
        var measure = new MeasureNode();
        var measureCapture = new MeasureCaptureNode();
        var preview = new PreviewNode();
        var output = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(shared);
        model.Nodes.Add(measure);
        model.Nodes.Add(measureCapture);
        model.Nodes.Add(preview);
        model.Nodes.Add(output);

        model.Connect(shared.Input, inputNode.Output);
        model.Connect(measure.Input, shared.Output);
        model.Connect(measureCapture.X, measure.X);
        model.Connect(measureCapture.Y, measure.Y);
        model.Connect(measureCapture.Width, measure.Width);
        model.Connect(measureCapture.Height, measure.Height);
        if (connectPreview)
            model.Connect(preview.Input, shared.Output);
        model.Connect(output.InputPort, shared.Output);

        return new UtilityGraph(
            (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default),
            shared,
            measureCapture,
            preview);
    }

    private static NodeMonitor<Ref<Bitmap>?> GetPreviewMonitor(PreviewNode node)
        => node.Items.OfType<NodeMonitor<Ref<Bitmap>?>>().Single();

    private static TargetCommandDescription[] RecordTargetCommands(RenderNode root, Rect targetDomain)
    {
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain,
            targetDomain,
            cachePolicy: RenderCacheOptions.Disabled));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        return graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Select(static reference => reference.Payload)
            .OfType<TargetCommandRenderFragmentPayload>()
            .Select(static payload => payload.Description)
            .ToArray();
    }

    private sealed record UtilityGraph(
        NodeGraphFilterEffect.Resource Resource,
        CountingPassThroughGraphNode Shared,
        MeasureCaptureNode MeasureCapture,
        PreviewNode Preview);

}

// A GPU-free FilterEffect whose render node stamps the resolved working scale onto pass-through fragments,
// exposing the scale that NodeGraphFilterEffectRenderNode forwarded.
[SuppressResourceClassGeneration]
internal sealed partial class ScaleProbeEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNode CreateRenderNode() => new ScaleProbeRenderNode(this);
    }
}

internal sealed class ScaleProbeRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public override void Process(RenderNodeContext context)
    {
        // Resolve w as FilterEffectRenderNode.Process would, but retain an identity opaque map so the
        // forwarded supply-driven scale can be observed without invoking a GPU filter during recording.
        foreach (RenderFragmentHandle input in context.Inputs)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
                execute: session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                hitTest: RenderHitTestContract.AnyInput,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Custom(
                    static metadata => RenderScaleUtilities.ResolveWorkingScale(
                        metadata.InputSupplies.ToArray(),
                        metadata.OutputScale,
                        metadata.MaxWorkingScale)));
            context.Publish(context.OpaqueMap(input, description));
        }
    }
}

internal sealed partial class MeasureCaptureNode : GraphNode
{
    public MeasureCaptureNode()
    {
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
        Width = AddInput<float>("Width");
        Height = AddInput<float>("Height");
    }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public InputPort<float> Width { get; }

    public InputPort<float> Height { get; }

    public Rect Value { get; private set; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = (MeasureCaptureNode)GetOriginal();
            node.Value = new Rect(X, Y, Width, Height);
        }
    }
}

internal sealed partial class FixedRenderNodeGraphNode : GraphNode
{
    public FixedRenderNodeGraphNode(RenderNode value)
    {
        Value = value;
        Output = AddOutput<RenderNode?>("Output");
    }

    public RenderNode Value { get; }

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = ((FixedRenderNodeGraphNode)GetOriginal()).Value;
        }
    }
}

internal sealed partial class SharedNonValueSubtreeGraphNode : GraphNode
{
    public SharedNonValueSubtreeGraphNode()
    {
        Output = AddOutput<RenderNode?>("Output");
    }

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        private ContainerRenderNode? _root;

        public override void Update(GraphCompositionContext context)
        {
            if (_root is null)
            {
                var shared = new OrderOnlyCommandRenderNode(new MixedPreviewGraphNode());
                var left = new ContainerRenderNode();
                var right = new ContainerRenderNode();
                left.AddChild(shared);
                right.AddChild(shared);
                _root = new ContainerRenderNode();
                _root.AddChild(left);
                _root.AddChild(right);
            }

            Output = _root;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                _root?.Dispose();
            _root = null;
        }
    }
}

internal sealed partial class CountingPassThroughGraphNode : GraphNode
{
    public CountingPassThroughGraphNode()
    {
        Input = AddInput<RenderNode?>("Input");
        Output = AddOutput<RenderNode?>("Output");
    }

    public InputPort<RenderNode?> Input { get; }

    public OutputPort<RenderNode?> Output { get; }

    public int ProcessCount { get; internal set; }

    public int EvaluationCount { get; internal set; }

    public partial class Resource
    {
        private NonOwningCountingContainerRenderNode? _renderNode;

        public override void Update(GraphCompositionContext context)
        {
            var node = (CountingPassThroughGraphNode)GetOriginal();
            node.EvaluationCount++;
            if (Input is null)
            {
                Output = null;
                return;
            }

            _renderNode ??= new NonOwningCountingContainerRenderNode(node);
            _renderNode.SetInput(Input);
            Output = _renderNode;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                _renderNode?.Dispose();
            _renderNode = null;
        }
    }
}

internal sealed class NonOwningCountingContainerRenderNode(CountingPassThroughGraphNode owner)
    : ContainerRenderNode
{
    private RenderNode? _input;

    public void SetInput(RenderNode input)
    {
        if (ReferenceEquals(_input, input))
            return;
        if (_input is not null)
            RemoveChild(_input);

        _input = input;
        AddChild(input);
    }

    public override void Process(RenderNodeContext context)
    {
        owner.ProcessCount++;
        base.Process(context);
    }

    protected override void OnDispose(bool disposing)
    {
        if (_input is not null)
            RemoveChild(_input);
        _input = null;
    }
}

internal sealed partial class MixedPreviewGraphNode : GraphNode
{
    public MixedPreviewGraphNode()
    {
        Input = AddInput<RenderNode?>("Input");
        Output = AddOutput<RenderNode?>("Output");
    }

    public InputPort<RenderNode?> Input { get; }

    public OutputPort<RenderNode?> Output { get; }

    public int CommandExecutionCount { get; internal set; }

    public partial class Resource
    {
        private NonOwningMixedContainerRenderNode? _renderNode;

        public override void Update(GraphCompositionContext context)
        {
            if (Input is null)
            {
                Output = null;
                return;
            }

            var node = (MixedPreviewGraphNode)GetOriginal();
            _renderNode ??= new NonOwningMixedContainerRenderNode(node);
            _renderNode.SetInput(Input);
            Output = _renderNode;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                _renderNode?.Dispose();
            _renderNode = null;
        }
    }
}

internal sealed class NonOwningMixedContainerRenderNode : ContainerRenderNode
{
    private readonly RenderNode _command;
    private RenderNode? _input;

    public NonOwningMixedContainerRenderNode(MixedPreviewGraphNode owner)
    {
        _command = new OrderOnlyCommandRenderNode(owner);
        AddChild(_command);
    }

    public void SetInput(RenderNode input)
    {
        if (ReferenceEquals(_input, input))
            return;
        if (_input is not null)
            RemoveChild(_input);

        _input = input;
        RemoveChild(_command);
        AddChild(input);
        AddChild(_command);
    }

    protected override void OnDispose(bool disposing)
    {
        if (_input is not null)
            RemoveChild(_input);
        RemoveChild(_command);
        _command.Dispose();
        _input = null;
    }
}

internal sealed class OrderOnlyCommandRenderNode(MixedPreviewGraphNode owner) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.TargetCommand([], TargetCommandDescription.CreateRequestLocal(
            _ => owner.CommandExecutionCount++,
            TargetRegion.Empty,
            Rect.Empty,
            RenderHitTestContract.None)));
    }
}

internal sealed class CountingOpaqueSourceRenderNode(Rect bounds) : RenderNode
{
    public int ExecutionCount { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
            session =>
            {
                ExecutionCount++;
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale)));
    }
}

internal sealed class ThrowingRenderNode : RenderNode
{
    public const string Message = "unrelated render-node failure";

    public int ProcessCount { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        ProcessCount++;
        throw new InvalidOperationException(Message);
    }
}

internal sealed class EmptyZeroOrOneRenderNode(Rect bounds) : RenderNode
{
    public int ExecutionCount { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
            _ => ExecutionCount++,
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.ZeroOrOne,
            RenderScaleContract.MaterializeAtWorkingScale)));
    }
}
