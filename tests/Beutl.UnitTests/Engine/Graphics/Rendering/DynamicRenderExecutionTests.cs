using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class DynamicRenderExecutionTests
{
    private static readonly Rect Domain = new(0, 0, 32, 24);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ContinuationExecutesRecordedSuffixWithoutReauthoring(int repetitions)
    {
        int recordings = 0, executions = 0;
        using var program = new FilterEffectContext(Domain);
        program.CustomEffect(repetitions, static (count, execution) =>
        {
            for (int i = 0; i < count; i++) execution.Continuation!.ApplyTo(execution.Targets);
        }, CustomFilterEffectOptions.ControlsContinuation);
        var tail = new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            recordings++;
            context.CustomEffect(0, (_, _) => executions++);
            context.Saturate(0);
        });
        using var resource = tail.ToResource(CompositionContext.Default);
        tail.ApplyTo(program, resource);
        using var target = new CpuRenderTarget(new PixelSize(32, 24));
        target.Value.Canvas.Clear(SKColors.Red);
        using var targets = new EffectTargets { new(target, Domain) };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary, null, targetDomain: Domain);
        executor.Apply(program);
        executor.Flush();
        Assert.That(recordings, Is.EqualTo(1));
        Assert.That(executions, Is.EqualTo(repetitions));
    }

    [Test]
    public void RepeatedContinuationSharesTheRenderersShaderProgramCache()
    {
        using var root = Effect(context =>
        {
            context.CustomEffect(0, static (_, execution) =>
            {
                execution.Continuation!.ApplyTo(execution.Targets);
                execution.Continuation.ApplyTo(execution.Targets);
            }, CustomFilterEffectOptions.ControlsContinuation);
            context.Shader(Beutl.Graphics.Shaders.ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                static bindings => bindings.Uniform("gain", 0.5f)));
        });
        using var renderer = Renderer(root);
        using (renderer.Rasterize()) { }
        Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(1));
        using (renderer.Rasterize()) { }
        Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(1));
        Assert.That(renderer.ProgramCacheStatistics.Hits, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void NestedContinuationAndCallbackLifetimeAreEnforced()
    {
        int calls = 0;
        FilterEffectContinuation? captured = null;
        using var node = Effect(context =>
        {
            context.CustomEffect(0, (_, execution) =>
            {
                captured = execution.Continuation;
                captured!.ApplyTo(execution.Targets);
                captured.ApplyTo(execution.Targets);
            }, CustomFilterEffectOptions.ControlsContinuation);
            context.CustomEffect(0, static (_, execution) =>
            {
                execution.Continuation!.ApplyTo(execution.Targets);
                execution.Continuation.ApplyTo(execution.Targets);
            }, CustomFilterEffectOptions.ControlsContinuation);
            context.CustomEffect(0, (_, _) => calls++);
        });
        using var renderer = Renderer(node);
        using var output = new CpuRenderTarget(new PixelSize(32, 24));
        using var canvas = new ImmediateCanvas(output, RenderIntent.Delivery, logicalSize: Domain.Size);
        renderer.Render(canvas);
        Assert.That(calls, Is.EqualTo(4));
        using var targets = new EffectTargets();
        Assert.That(() => captured!.ApplyTo(targets), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void ExecutionAccessExpiresBeforeTheNextCallbackInTheSameProgram()
    {
        RenderExecutionContext? captured = null;
        using var root = Effect(context =>
        {
            context.CustomEffect(0, (_, execution) => captured = execution.ExecutionContext,
                CustomFilterEffectOptions.AccessesTarget);
            context.CustomEffect(0, (_, _) =>
                Assert.That(() => captured!.CaptureTarget(), Throws.TypeOf<ObjectDisposedException>()));
        });
        using var renderer = Renderer(root);
        using var result = renderer.Rasterize();
    }

    [Test]
    public void DependenciesShareAllocationPolicyAndAreReusedWithinTheRequest()
    {
        int childCalls = 0;
        RenderExecutionContext? captured = null;
        RenderTargetImageLease? escaped = null;
        using var child = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            childCalls++;
            Assert.That(execution.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(execution.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            Assert.That(execution.WorkingScale, Is.LessThanOrEqualTo(1));
        }));
        var childDomain = new Rect(0, 0, 16, 12);
        using var root = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            captured = execution.ExecutionContext!;
            escaped = captured.RenderDependency(child, ("layer", 3), childDomain);
            using var second = captured.RenderDependency(child, ("layer", 3), childDomain);
            Assert.That(second.Image.Width, Is.EqualTo(16));
            Assert.That(captured.CanAccessTarget, Is.False);
            Assert.That(() => captured.CaptureTarget(), Throws.InvalidOperationException);
        }));
        var factory = new CountingFactory();
        using var renderer = Renderer(root, factory);
        using var target = new CpuRenderTarget(new PixelSize(32, 24));
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: Domain.Size);
        try
        {
            renderer.Render(canvas);
            Assert.That(childCalls, Is.EqualTo(1));
            Assert.That(factory.Sizes, Does.Contain(new PixelSize(16, 12)));
            Assert.That(escaped!.Image.Width, Is.EqualTo(16), "A returned image survives request cleanup.");
            Assert.That(() => captured!.RenderDependency(child, "late", childDomain),
                Throws.TypeOf<ObjectDisposedException>());
        }
        finally { escaped?.Dispose(); }
    }

    [Test]
    public void EmptyDependencyReturnsATransparentImageAtTheRequestedSize()
    {
        using var empty = new ContainerRenderNode();
        using var root = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            using var result = execution.ExecutionContext!.RenderDependency(empty, "empty", Domain);
            using var pixels = SKBitmap.FromImage(result.Image);
            Assert.That(pixels.Width, Is.EqualTo(32));
            Assert.That(pixels.Height, Is.EqualTo(24));
            Assert.That(pixels.Pixels.All(static pixel => pixel.Alpha == 0), Is.True);
        }));
        using var renderer = Renderer(root);
        using var output = renderer.Rasterize();
    }

    [Test]
    public void TargetEffectsPreserveTheDeclaredWorkingScalePolicy()
    {
        int calls = 0;
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, execution) =>
            {
                calls++;
                Assert.That(execution.WorkingScale, Is.EqualTo(0.5f));
                Assert.That(execution.ExecutionContext!.WorkingScale, Is.EqualTo(0.5f));
            }, CustomFilterEffectOptions.AccessesTarget));
        using var root = new ScaledFilterNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(Domain, Brushes.Resource.Red, null));
        using var renderer = Renderer(root);
        using var output = renderer.Rasterize();
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(1f, 2f)]
    [TestCase(1f, 0.5f)]
    [TestCase(0.6f, 1f)]
    [TestCase(0.6f, 0.5f)]
    public void DeclaringTargetAccessPreservesTheOrdinaryEffectRasterFootprint(float density, float boundsFactor)
    {
        SKColor[] Render(bool accessesTarget)
        {
            var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
                context.CustomEffect(0, (_, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                    {
                        // Isolate output placement from how each path rasterizes the input edges.
                        SKCanvas pixels = target.RenderTarget!.Value.Canvas;
                        pixels.Clear(SKColors.Blue);
                        using var stripe = new SKPaint { Color = SKColors.Red };
                        pixels.DrawRect(0, 0, 2, target.RenderTarget.Height, stripe);
                        target.Bounds = new Rect(target.Bounds.Position, target.Bounds.Size * boundsFactor);
                    }
                }, accessesTarget ? CustomFilterEffectOptions.AccessesTarget : CustomFilterEffectOptions.None));
            using var root = new ScaledFilterNode(effect.ToResource(CompositionContext.Default), density);
            root.AddChild(new RectangleRenderNode(new Rect(4, 3, 11, 7), Brushes.Resource.Blue, null));
            using var renderer = Renderer(root);
            using var target = new CpuRenderTarget(new PixelSize(32, 24));
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: Domain.Size);
            renderer.Render(canvas);
            using var image = target.AcquireImage();
            using var pixels = SKBitmap.FromImage(image.Image);
            return pixels.Pixels;
        }

        SKColor[] expected = Render(false);
        Assert.That(expected.Any(pixel => pixel.Alpha != 0), Is.True);
        Assert.That(Render(true), Is.EqualTo(expected));
    }

    [Test]
    public void DynamicCycleFailsAndTheNextRequestCanRun()
    {
        bool recurse = true;
        FilterEffectRenderNode? child = null;
        child = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            if (recurse)
                using (execution.ExecutionContext!.RenderDependency(child!, "cycle", Domain)) { }
        }));
        using (child)
        using (var root = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            using var dependency = execution.ExecutionContext!.RenderDependency(child, "cycle", Domain);
        })))
        using (var renderer = Renderer(root))
        using (var target = new CpuRenderTarget(new PixelSize(32, 24)))
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: Domain.Size))
        {
            Assert.That(() => renderer.Render(canvas), Throws.Exception.With.Message.Contains("Circular"));
            recurse = false;
            Assert.DoesNotThrow(() => renderer.Render(canvas));
        }
    }

    [Test]
    public void DeclaredTargetEffectSamplesAndErasesPrecedingContent()
    {
        RenderExecutionContext? captured = null;
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(Domain, Brushes.Resource.Red, null));
        root.AddChild(Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            captured = execution.ExecutionContext!;
            using var before = captured.CaptureTarget();
            using var pixels = SKBitmap.FromImage(before.Image);
            Assert.That(pixels.GetPixel(15, 12).Red, Is.GreaterThan(240));
            using var blank = new SKBitmap(32, 24);
            blank.Erase(SKColors.Transparent);
            using var image = SKImage.FromBitmap(blank);
            captured.ReplaceTarget(image);
            execution.Targets.Clear();
        }, CustomFilterEffectOptions.AccessesTarget)));
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.Lime, null));
        using var renderer = Renderer(root);
        using var target = new CpuRenderTarget(new PixelSize(32, 24));
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: Domain.Size);
        renderer.Render(canvas);
        using var result = target.AcquireImage();
        using var bitmap = SKBitmap.FromImage(result.Image);
        Assert.That(bitmap.GetPixel(15, 12).Alpha, Is.Zero);
        Assert.That(bitmap.GetPixel(1, 1).Green, Is.GreaterThan(240));
        Assert.That(() => captured!.CaptureTarget(), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void CaughtDependencyFailureStillFailsTheDeliveryRequest()
    {
        using var child = Effect(context => context.CustomEffect(0,
            static (_, _) => throw new InvalidOperationException("dependency failed")));
        using var root = Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            try { using var result = execution.ExecutionContext!.RenderDependency(child, "failure", Domain); }
            catch (InvalidOperationException) { }
        }));
        using var renderer = Renderer(root);
        using var target = new CpuRenderTarget(new PixelSize(32, 24));
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: Domain.Size);
        Assert.That(() => renderer.Render(canvas), Throws.Exception.With.Message.Contains("dependency failed"));
    }

    [Test]
    public void UnplannedRendererLaunchRemainsForbidden()
    {
        using var child = new RectangleRenderNode(Domain, Brushes.Resource.Red, null);
        using var nested = Renderer(child);
        using var root = Effect(context => context.CustomEffect(0, (_, _) =>
            Assert.That(() => nested.Rasterize(), Throws.InvalidOperationException.With.Message.Contains("cannot be launched"))));
        using var renderer = Renderer(root);
        using var result = renderer.Rasterize();
    }

    [Test]
    public void TargetCommandInputImageSurvivesTheCommandWhenItsLeaseEscapes()
    {
        RenderTargetImageLease? retained = null;
        using var source = Effect(context => context.CustomEffect(0, (_, execution) =>
            retained = execution.Targets[0].RenderTarget!.AcquireImage()));
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, static (_, _) => { }, CustomFilterEffectOptions.AccessesTarget));
        using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(source);
        using var renderer = Renderer(root);
        try
        {
            using var result = renderer.Rasterize();
            Assert.That(retained, Is.Not.Null);
            Assert.That(retained!.Image.Handle, Is.Not.EqualTo(IntPtr.Zero));
            using var pixels = SKBitmap.FromImage(retained.Image);
            Assert.That(pixels.GetPixel(0, 0), Is.EqualTo(SKColors.Blue));
        }
        finally { retained?.Dispose(); }
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void DependencyAllocationFailureFollowsTheRequestIntent(RenderIntent intent)
    {
        var childDomain = new Rect(0, 0, 17, 13);
        using var child = new RectangleRenderNode(childDomain, Brushes.Resource.Red, null);
        using var root = new ContainerRenderNode();
        root.AddChild(Effect(context => context.CustomEffect(0, (_, execution) =>
        {
            using var image = execution.ExecutionContext!.RenderDependency(child, "unavailable", childDomain);
        })));
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.Lime, null));
        var factory = new CountingFactory { RejectedSize = new PixelSize(17, 13) };
        using var renderer = Renderer(root, factory, intent);
        using var target = new CpuRenderTarget(new PixelSize(32, 24));
        using var canvas = new ImmediateCanvas(target, intent, logicalSize: Domain.Size);
        if (intent == RenderIntent.Delivery)
            Assert.That(() => renderer.Render(canvas), Throws.Exception);
        else
        {
            renderer.Render(canvas);
            using var image = target.AcquireImage();
            using var pixels = SKBitmap.FromImage(image.Image);
            Assert.That(pixels.GetPixel(1, 1), Is.EqualTo(SKColors.Lime));
            Assert.That(pixels.GetPixel(20, 20).Alpha, Is.Zero);
        }
        Assert.That(factory.Sizes, Does.Contain(new PixelSize(17, 13)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CallbackAndDependenciesUseTheDensityAfterRuntimeExpansion(bool continuation)
    {
        using var target = new CpuRenderTarget(new PixelSize(8, 8));
        using var targets = new EffectTargets { new(target, new Rect(0, 0, 8, 8)) };
        using var builder = new SKImageFilterBuilder();
        using var execution = new RenderExecutionContext((_, _, _, scale) =>
        {
            Assert.That(scale, Is.EqualTo(0.5f));
            return target.AcquireImage();
        }, 1, 1);
        using var program = new FilterEffectContext(new Rect(0, 0, 8, 8));
        if (continuation)
            program.CustomEffect(0, static (_, context) => context.Continuation!.ApplyTo(context.Targets),
                CustomFilterEffectOptions.ControlsContinuation);
        program.CustomEffect(0, static (_, context) => context.Targets[0].Bounds = new Rect(0, 0, 128, 8));
        int calls = 0;
        program.CustomEffect(0, (_, context) =>
        {
            calls++;
            Assert.That(context.WorkingScale, Is.EqualTo(0.5f));
            Assert.That(context.ExecutionContext!.WorkingScale, Is.EqualTo(context.WorkingScale));
            using var child = new ContainerRenderNode();
            using var image = context.ExecutionContext.RenderDependency(child, "density", Domain);
        });
        using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Delivery,
            RenderRequestPurpose.Frame, 1, 1, 1, default, budget: BufferDimensionBudget.Named(64))
        { ExecutionContext = execution };
        executor.Apply(program);
        Assert.That(calls, Is.EqualTo(1));
    }

    private static FilterEffectRenderNode Effect(Action<FilterEffectContext> record)
    {
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) => record(context));
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new RectangleRenderNode(Domain, Brushes.Resource.Blue, null));
        return node;
    }

    private static RenderNodeRenderer Renderer(RenderNode root, IRenderTargetFactory? factory = null, RenderIntent intent = RenderIntent.Delivery)
        => new(root, new RenderNodeRenderRequest
        {
            Intent = intent,
            Purpose = RenderRequestPurpose.Frame,
            TargetDomain = Domain,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
        }, factory ?? new CpuRenderTargetFactory());

    private sealed class ScaledFilterNode(FilterEffect.Resource resource, float density = 0.5f) : FilterEffectRenderNode(resource)
    {
        protected override RenderScaleContract? GetWorkingScaleContract()
            => RenderScaleContract.Custom(density, static (value, _) => value);
    }

    private sealed class CountingFactory : IRenderTargetFactory
    {
        public PixelSize? RejectedSize { get; init; }
        public List<PixelSize> Sizes { get; } = [];
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Sizes.Add(allocation.DeviceSize);
            return allocation.DeviceSize == RejectedSize ? null : new CpuRenderTarget(allocation.DeviceSize);
        }
    }
}
