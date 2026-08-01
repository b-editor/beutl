using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Threading;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RendererWideRecordingTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionFrameRenderer_PreservesPlanLifetimeAcrossFrames()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(8, 8);
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));

            renderer.Render(frame);
            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Misses, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Hits, Is.EqualTo(1));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionFrameRenderer_DisablesDiagnosticsByDefault()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(8, 8);

            Assert.That(renderer.Diagnostics, Is.Null);
        });
    }

    [Test]
    public void ProductionFrameRenderer_UsesExplicitDiagnostics()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var diagnostics = new RenderPipelineDiagnosticsState();
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: diagnostics,
                surface: new CpuRenderTarget(8, 8));
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.Diagnostics, Is.SameAs(diagnostics));
                Assert.That(diagnostics.LatestFrame.Succeeded, Is.True);
                Assert.That(diagnostics.LatestFrame.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
            });
        });
    }

    [Test]
    [NonParallelizable]
    public void ProductionFrameRenderer_DisposedFromOwnerThread_ReleasesSurfaceOnRenderThread()
    {
        var surface = new DisposalThreadProbeRenderTarget(8, 8);
        var renderer = new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            diagnostics: null,
            surface: surface);
        var frame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(8, 8));
        RenderThread.Dispatcher.Invoke(() => renderer.Render(frame));

        Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False,
            "the fixture must dispose from the renderer owner's non-render thread");
        renderer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(surface.DisposeCount, Is.EqualTo(1));
            Assert.That(surface.DisposedOnRenderThread, Is.True);
        });
    }

    [Test]
    [NonParallelizable]
    public void ProductionFrameRenderer_FinalizerReleasesOwnedResourcesOnRenderThread()
    {
        var state = new DisposalThreadState();
        WeakReference renderer = AbandonRenderer(state);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        RenderThread.Dispatcher.Invoke(static () => { });
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(renderer.IsAlive, Is.False);
            Assert.That(state.DisposeCount, Is.EqualTo(1));
            Assert.That(state.DisposedOnRenderThread, Is.True);
        });
    }

    [Test]
    [NonParallelizable]
    public void ProductionFrameRenderer_FinalizerCallsDerivedHookInlineBeforeRenderThreadCleanup()
    {
        var state = new FinalizerHookState();
        WeakReference renderer = AbandonFinalizerHookProbe(state);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        bool hookCompletedBeforeRenderThreadBarrier = state.Completed;
        RenderThread.Dispatcher.Invoke(static () => { });
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(renderer.IsAlive, Is.False);
            Assert.That(hookCompletedBeforeRenderThreadBarrier, Is.True);
            Assert.That(state.CallCount, Is.EqualTo(1));
            Assert.That(state.CalledOnRenderThread, Is.False);
        });
    }

    [Test]
    [NonParallelizable]
    public void ProductionFrameRenderer_FailedConstruction_FinalizerCleanupKeepsRenderThreadAlive()
    {
        Dispatcher dispatcher = RenderThread.Dispatcher;
        Exception? unhandledException = null;
        EventHandler<DispatcherUnhandledExceptionEventArgs> handler = (_, args) =>
        {
            Interlocked.CompareExchange(ref unhandledException, args.Exception, null);
            args.Handled = true;
        };
        dispatcher.UnhandledException += handler;
        try
        {
            FailRendererConstructionAfterRenderResourcesAreCreated();
            FailRendererConstructionBeforeFrameRendererIsAssigned();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            dispatcher.Invoke(static () => { });
        }
        finally
        {
            dispatcher.UnhandledException -= handler;
        }

        Assert.That(Volatile.Read(ref unhandledException), Is.Null);
    }

    [Test]
    [NonParallelizable]
    public void ClearAllCaches_QueuedBeforeDispose_DoesNotReplaceDisposedFrameRenderer()
    {
        var renderer = new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            diagnostics: null,
            surface: new CpuRenderTarget(8, 8));
        FieldInfo frameRendererField = typeof(Renderer).GetField(
            "_frameRenderer",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initialFrameRenderer = (RenderNodeRenderer)frameRendererField.GetValue(renderer)!;
        var renderThreadBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRenderThread = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? clearFailure = null;
        var clearThread = new Thread(() =>
        {
            clearStarted.TrySetResult();
            try
            {
                renderer.ClearAllCaches();
            }
            catch (Exception ex)
            {
                clearFailure = ex;
            }
        })
        {
            IsBackground = true,
        };
        bool renderThreadWasBlocked = false;
        bool clearDidStart = false;
        bool clearWasQueued = false;
        bool blockerWasDrained;
        bool clearCompleted;
        bool renderThreadWasDrained = false;
        try
        {
            RenderThread.Dispatcher.Dispatch(
                () =>
                {
                    try
                    {
                        renderThreadBlocked.TrySetResult();
                        releaseRenderThread.Task.GetAwaiter().GetResult();
                    }
                    finally
                    {
                        blockerCompleted.TrySetResult();
                    }
                },
                DispatchPriority.High);
            renderThreadWasBlocked = renderThreadBlocked.Task.Wait(TimeSpan.FromSeconds(5));

            clearThread.Start();
            clearDidStart = clearStarted.Task.Wait(TimeSpan.FromSeconds(5));
            clearWasQueued = SpinWait.SpinUntil(
                () => (clearThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));

            RenderThread.Dispatcher.Dispatch(renderer.Dispose, DispatchPriority.High);
        }
        finally
        {
            releaseRenderThread.TrySetResult();
            blockerWasDrained = blockerCompleted.Task.Wait(TimeSpan.FromSeconds(5));
            clearCompleted = (clearThread.ThreadState & ThreadState.Unstarted) != 0
                || clearThread.Join(TimeSpan.FromSeconds(5));
            if (blockerWasDrained)
            {
                renderThreadWasDrained = RenderThread.Dispatcher
                    .InvokeAsync(static () => { })
                    .Wait(TimeSpan.FromSeconds(5));
            }
        }

        Exception[] failures = clearFailure is AggregateException aggregate
            ? [.. aggregate.Flatten().InnerExceptions]
            : clearFailure is null ? [] : [clearFailure];

        Assert.Multiple(() =>
        {
            Assert.That(renderThreadWasBlocked, Is.True);
            Assert.That(clearDidStart, Is.True);
            Assert.That(clearWasQueued, Is.True);
            Assert.That(blockerWasDrained, Is.True);
            Assert.That(clearCompleted, Is.True);
            Assert.That(renderThreadWasDrained, Is.True);
            Assert.That(failures.Any(static ex => ex is ObjectDisposedException), Is.True);
            Assert.That(frameRendererField.GetValue(renderer), Is.SameAs(initialFrameRenderer));
            Assert.That(initialFrameRenderer.IsDisposed, Is.True);
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionFrameRenderer_ClearAllCachesColdResetsFrameCachesWithoutChangingPolicy()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var state = new RendererWideTreeState(1) { UseShaderProgram = true };
            var drawable = new RendererWideProbeDrawable(0, state);
            using Drawable.Resource resource =
                (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(8, 8)
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };
            RenderCacheOptions expectedOptions = renderer.CacheOptions;

            renderer.Render(frame);
            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.RetainedPlans, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Hits, Is.EqualTo(1));
                Assert.That(renderer.FrameProgramCacheStatistics.RetainedPrograms, Is.GreaterThan(0));
                Assert.That(renderer.FrameTargetPoolStatistics.RetainedBytes, Is.GreaterThan(0));
            });

            renderer.ClearAllCaches();

            Assert.Multiple(() =>
            {
                Assert.That(renderer.CacheOptions, Is.SameAs(expectedOptions));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics, Is.EqualTo(default(StructuralPlanCacheStatistics)));
                Assert.That(renderer.FrameProgramCacheStatistics, Is.EqualTo(default(ProgramCacheStatistics)));
                Assert.That(renderer.FrameTargetPoolStatistics, Is.EqualTo(default(RenderTargetPoolStatistics)));
            });

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Misses, Is.EqualTo(1));
                Assert.That(renderer.FrameStructuralPlanCacheStatistics.Hits, Is.Zero);
                Assert.That(renderer.FrameProgramCacheStatistics.RetainedPrograms, Is.GreaterThan(0));
                Assert.That(renderer.FrameTargetPoolStatistics.RetainedBytes, Is.GreaterThan(0));
            });
        });
    }

    [Test]
    [NonParallelizable]
    [Category("GpuPassFusionGpu")]
    public void CacheMutations_FromCallerThread_DisposeCachedTreesOnRenderThread()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var state = new CacheMutationThreadState();
        var drawable = new CacheMutationThreadProbeDrawable(state);
        using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(8, 8));
        Renderer renderer = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var result = new Renderer(8, 8);
            result.Render(frame);
            return result;
        });

        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);

            renderer.ClearAllCaches();
            Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true }));

            VulkanTestEnvironment.InvokeOnRenderThread(() => renderer.Render(frame));
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            Assert.Multiple(() =>
            {
                Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true, true }));
                Assert.That(renderer.CacheOptions, Is.EqualTo(RenderCacheOptions.Disabled));
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(renderer.Dispose);
        }
    }

    [Test]
    [NonParallelizable]
    [Category("GpuPassFusionGpu")]
    public void Detachment_FromCallerThread_DisposesCachedTreeOnRenderThread()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var state = new CacheMutationThreadState();
        var root = new CacheMutationHierarchyRoot();
        var drawable = new CacheMutationThreadProbeDrawable(state);
        root.Attach(drawable);
        using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(8, 8));
        Renderer renderer = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var result = new Renderer(8, 8);
            result.Render(frame);
            return result;
        });

        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);

            root.Detach(drawable);
            VulkanTestEnvironment.InvokeOnRenderThread(static () => { });

            Assert.That(state.DisposedOnRenderThread, Is.EqualTo(new[] { true }));
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(renderer.Dispose);
        }
    }

    [Test]
    public void CompleteTarget_RecordsEveryOrderedRootBeforeAnyExecution()
    {
        bool[] recorded = new bool[3];
        var executed = new List<int>();
        using var first = new DeferredProbeNode(0, recorded, executed);
        using var second = new DeferredProbeNode(1, recorded, executed);
        using var third = new DeferredProbeNode(2, recorded, executed);
        using var completeTarget = new CompleteTargetRenderNode(first, [second, third]);
        using var destination = new CpuRenderTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        using var renderer = new RenderNodeRenderer(
            completeTarget,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.All.True);
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public void CompleteTarget_RecordsClearCommandsCaptureAndPainterOrderBeforeExecution()
    {
        bool[] recorded = new bool[4];
        var executed = new List<string>();
        using var clear = new RecordingRootNode(0, recorded, new ClearRenderNode(Colors.Transparent));
        using var source = new OrderedSourceNode(1, recorded, executed);
        using var command = new OrderedTargetCommandNode(2, recorded, executed);
        using var capture = new OrderedCaptureNode(3, recorded, executed);
        using var completeTarget = new CompleteTargetRenderNode(clear, [source, command, capture]);
        using var destination = new CpuRenderTarget(8, 8);
        using var canvas = new ImmediateCanvas(destination);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            completeTarget,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        renderer.Render(canvas);

        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        int lastRecorded = snapshot.Events.ToList().FindLastIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.FragmentRecorded);
        int firstExecutedPass = snapshot.Events.ToList().FindIndex(
            static item => item.Kind == RenderPipelineDiagnosticEventKind.PassExecuted);
        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.All.True);
            Assert.That(executed, Is.EqualTo(new[] { "source", "command", "capture" }));
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCommands], Is.EqualTo(2),
                "The complete request must include both the root clear and authored target command.");
            Assert.That(snapshot[RenderPipelineCounter.RecordedTargetCaptures], Is.EqualTo(1));
            Assert.That(firstExecutedPass, Is.GreaterThan(lastRecorded),
                "Every complete-target fragment must be committed before planner-controlled execution starts.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProductionRenderer_BuildsRecordsAndCommitsEveryTreeAsOneRequest()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var state = new RendererWideTreeState(2);
            var first = new RendererWideProbeDrawable(0, state);
            var second = new RendererWideProbeDrawable(1, state);
            var resources = ImmutableArray.Create<EngineObject.Resource>(
                (Drawable.Resource)first.ToResource(CompositionContext.Default),
                (Drawable.Resource)second.ToResource(CompositionContext.Default));
            var frame = new CompositionFrame(
                resources,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(8, 8);

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(state.BuildCalls, Is.EqualTo(new[] { 1, 1 }));
                Assert.That(state.RecordCalls, Is.EqualTo(new[] { 1, 1 }),
                    "Rendering must record each drawable tree only once in the complete frame request.");
                Assert.That(state.FrameRecordCalls, Is.EqualTo(new[] { 1, 1 }));
                Assert.That(state.ExecutionOrder, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(state.Nodes, Has.All.Not.Null);
                Assert.That(state.Nodes, Has.All.Matches<ProductionTreeProbeNode>(
                    static node => !node.HasChanges && node.Cache.CanCache()),
                    "Successful complete-request execution must commit every tree's render-count/cache state.");
            });
        });
    }

    [Test]
    public void ProductionRenderer_LazilyCachesBoundariesForCurrentFrame()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var state = new RendererWideTreeState(2);
            var first = new RendererWideProbeDrawable(0, state);
            var second = new RendererWideProbeDrawable(1, state);
            using Drawable.Resource firstResource =
                (Drawable.Resource)first.ToResource(CompositionContext.Default);
            using Drawable.Resource secondResource =
                (Drawable.Resource)second.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(firstResource, secondResource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: null,
                surface: new CpuRenderTarget(8, 8))
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };
            var expectedBounds = new Rect(0, 0, 8, 8);

            renderer.Render(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 1, 1 }));

            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 1 }));
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 1 }),
                "A repeated single-drawable query must reuse the current-frame bounds.");

            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }),
                "A repeated layer query must reuse every current-frame bound.");

            renderer.UpdateFrame(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(renderer.GetBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 3, 3 }),
                "Updating the current frame must invalidate every lazy bound.");

            renderer.Render(frame);
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 4, 4 }));
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 5, 4 }),
                "A successful render must invalidate every lazy bound.");
            Assert.That(renderer.GetBoundary(first), Is.EqualTo(expectedBounds));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 5, 4 }));

            Assert.That(renderer.RecalculateBoundaries(0), Is.EqualTo(new[] { expectedBounds, expectedBounds }));
            Assert.That(state.RecordCalls, Is.EqualTo(new[] { 6, 5 }),
                "Forced recalculation must record every matching drawable even when one bound is cached.");
            Assert.That(state.ExecutionOrder, Is.EqualTo(new[] { 0, 1, 0, 1 }));

            renderer.ClearAllCaches();
            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundaries(0), Is.Empty);
                Assert.That(renderer.GetBoundary(first), Is.Null);
                Assert.That(state.RecordCalls, Is.EqualTo(new[] { 6, 5 }),
                    "Clearing caches must not measure disposed current-frame entries.");
            });
        });
    }

    [Test]
    public void ProductionRenderer_UsesQueryBoundsForAFullTargetDrawable()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { CurrentValue = 3 },
                Height = { CurrentValue = 2 },
                AlignmentX = { CurrentValue = AlignmentX.Left },
                AlignmentY = { CurrentValue = AlignmentY.Top },
                Transform = { CurrentValue = new TranslateTransform(2, 1) },
                Fill = { CurrentValue = Brushes.White },
            });
            using Drawable.Resource resource =
                (Drawable.Resource)group.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(8, 8));
            using var renderer = new Renderer(
                width: 8,
                height: 8,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                diagnostics: null,
                surface: new CpuRenderTarget(8, 8))
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };

            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundary(group), Is.EqualTo(new Rect(2, 1, 3, 2)));
                Assert.That(renderer.RecalculateBoundaries(0), Is.EqualTo(new[] { new Rect(2, 1, 3, 2) }));
            });
        });
    }

    [Test]
    public void BoundaryCollectionQueries_RequireRenderThreadAccess()
    {
        Renderer renderer = RenderThread.Dispatcher.Invoke(() => new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            diagnostics: null,
            surface: new CpuRenderTarget(8, 8)));
        try
        {
            Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False);
            Assert.Throws<InvalidOperationException>(() => renderer.GetBoundaries(0));
            Assert.Throws<InvalidOperationException>(() => renderer.RecalculateBoundaries(0));
        }
        finally
        {
            RenderThread.Dispatcher.Invoke(renderer.Dispose);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonRenderer(DisposalThreadState state)
    {
        var surface = new DisposalThreadProbeRenderTarget(8, 8, state);
        GC.SuppressFinalize(surface);
        var renderer = new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            diagnostics: null,
            surface: surface);
        return new WeakReference(renderer, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonFinalizerHookProbe(FinalizerHookState state)
    {
        var renderer = new FinalizerHookProbeRenderer(state);
        return new WeakReference(renderer, trackResurrection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailRendererConstructionAfterRenderResourcesAreCreated()
    {
        var exception = Assert.Throws<AggregateException>(() => new Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            diagnostics: null,
            surface: new CpuRenderTarget(7, 8)));
        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailRendererConstructionBeforeFrameRendererIsAssigned()
    {
        Assert.Throws<ArgumentException>(() => new Renderer(
            width: 0,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1));
    }

    private sealed class RecordingRootNode(
        int index,
        bool[] recorded,
        RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            context.PublishRange(context.RecordNode(child, []));
        }

        protected override void OnDispose(bool disposing)
        {
            child.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class OrderedSourceNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            context.Publish(context.OpaqueSource(CreateDescription(
                "source",
                recorded,
                executed,
                static (session, output) =>
                    output.Canvas.Use(canvas => canvas.Clear(new Color(255, 40, 80, 120))))));
        }
    }

    private sealed class OrderedTargetCommandNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            TargetCommandDescription description = TargetCommandDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True);
                    executed.Add("command");
                    session.Canvas.Use(canvas => canvas.Clear(new Color(255, 24, 48, 72)));
                },
                TargetRegion.Full,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: typeof(OrderedTargetCommandNode),
                runtimeIdentity: new RenderRuntimeIdentity("ordered-command"));
            context.Publish(context.TargetCommand([], description));
        }
    }

    private sealed class OrderedCaptureNode(
        int index,
        bool[] recorded,
        ICollection<string> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            Rect bounds = new(0, 0, 8, 8);
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Full,
                bounds,
                RenderHitTestContract.None,
                RenderScaleContract.MaterializeAtWorkingScale));
            OpaqueRenderDescription replay = OpaqueRenderDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True);
                    executed.Add("capture");
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(session.Inputs.Single().Draw);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: typeof(OrderedCaptureNode),
                runtimeIdentity: new RenderRuntimeIdentity("ordered-capture"));
            context.Publish(context.ContributeValues(context.OpaqueMap(capture, replay)));
        }
    }

    private static OpaqueRenderDescription CreateDescription(
        string name,
        bool[] recorded,
        ICollection<string> executed,
        Action<OpaqueRenderSession, OpaqueRenderOutput> draw)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                Assert.That(recorded, Is.All.True);
                executed.Add(name);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                draw(session, output);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(RendererWideRecordingTests), name),
            runtimeIdentity: new RenderRuntimeIdentity(name));
    }

    private sealed class DeferredProbeNode(
        int index,
        bool[] recorded,
        List<int> executed) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            recorded[index] = true;
            var description = OpaqueRenderDescription.Create(
                session =>
                {
                    Assert.That(recorded, Is.All.True,
                        "No planner-controlled 2D callback may run until every target root is recorded.");
                    executed.Add(index);
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(new Color(255, 32, 64, 96)));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: (typeof(DeferredProbeNode), index),
                runtimeIdentity: new RenderRuntimeIdentity(index));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CacheMutationHierarchyRoot : Hierarchical, IHierarchicalRoot
    {
        public event EventHandler<IHierarchical>? DescendantAttached;

        public event EventHandler<IHierarchical>? DescendantDetached;

        public void Attach(IHierarchical child)
        {
            ((IModifiableHierarchical)this).AddChild(child);
        }

        public void Detach(IHierarchical child)
        {
            ((IModifiableHierarchical)this).RemoveChild(child);
        }

        public void OnDescendantAttached(IHierarchical descendant)
        {
            DescendantAttached?.Invoke(this, descendant);
        }

        public void OnDescendantDetached(IHierarchical descendant)
        {
            DescendantDetached?.Invoke(this, descendant);
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);

    private sealed class DisposalThreadProbeRenderTarget(
        int width,
        int height,
        DisposalThreadState? state = null)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height)
    {
        private readonly DisposalThreadState _state = state ?? new DisposalThreadState();

        public int DisposeCount => _state.DisposeCount;

        public bool DisposedOnRenderThread => _state.DisposedOnRenderThread;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                _state.DisposeCount++;
                _state.DisposedOnRenderThread = RenderThread.Dispatcher.CheckAccess();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DisposalThreadState
    {
        public int DisposeCount { get; set; }

        public bool DisposedOnRenderThread { get; set; }
    }

    private sealed class FinalizerHookProbeRenderer(FinalizerHookState state)
        : Renderer(
            width: 8,
            height: 8,
            renderScale: 1,
            maxWorkingScale: 1,
            diagnostics: null,
            surface: new CpuRenderTarget(8, 8))
    {
        protected override void OnDispose(bool disposing)
        {
            if (!disposing)
                state.Record();

            base.OnDispose(disposing);
        }
    }

    private sealed class FinalizerHookState
    {
        private int _callCount;
        private int _calledOnRenderThread;
        private int _completed;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool CalledOnRenderThread => Volatile.Read(ref _calledOnRenderThread) != 0;

        public bool Completed => Volatile.Read(ref _completed) != 0;

        public void Record()
        {
            if (RenderThread.Dispatcher.CheckAccess())
                Volatile.Write(ref _calledOnRenderThread, 1);

            Interlocked.Increment(ref _callCount);
            Volatile.Write(ref _completed, 1);
        }
    }
}

internal sealed class RendererWideTreeState(int count)
{
    public int[] BuildCalls { get; } = new int[count];

    public int[] RecordCalls { get; } = new int[count];

    public int[] FrameRecordCalls { get; } = new int[count];

    public List<int> ExecutionOrder { get; } = [];

    public ProductionTreeProbeNode?[] Nodes { get; } = new ProductionTreeProbeNode[count];

    public bool UseShaderProgram { get; init; }
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class RendererWideProbeDrawable(
    int index,
    RendererWideTreeState state) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        state.BuildCalls[index]++;
        var node = new ProductionTreeProbeNode(index, state);
        node.Cache.ReportRenderCount(RenderNodeCache.Count - 1);
        state.Nodes[index] = node;
        context.DrawNode(node);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(8, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class ProductionTreeProbeNode(
    int index,
    RendererWideTreeState state) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        int completedBuildCount = state.BuildCalls[index];
        Assert.That(completedBuildCount, Is.GreaterThan(0));
        Assert.That(state.BuildCalls, Is.All.EqualTo(completedBuildCount),
            "Every drawable tree must be built before the complete request starts recording.");
        state.RecordCalls[index]++;
        if (context.Purpose == RenderRequestPurpose.Frame)
        {
            state.FrameRecordCalls[index]++;
        }

        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                int completedFrameRecordCount = state.FrameRecordCalls[index];
                Assert.That(completedFrameRecordCount, Is.GreaterThan(0));
                Assert.That(state.FrameRecordCalls, Is.All.EqualTo(completedFrameRecordCount),
                    "Every top-level tree must be recorded before the first execution callback.");
                state.ExecutionOrder.Add(index);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(
                    index == 0
                        ? new Color(160, 96, 32, 16)
                        : new Color(160, 16, 64, 128)));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(ProductionTreeProbeNode), index),
            runtimeIdentity: new RenderRuntimeIdentity(index));
        RenderFragmentHandle source = context.OpaqueSource(description);
        if (state.UseShaderProgram)
        {
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return half4(color.b, color.g, color.r, color.a); }");
            context.Publish(context.Shader(source, shader));
        }
        else
        {
            context.Publish(source);
        }
    }
}

internal sealed class CacheMutationThreadState
{
    public List<bool> DisposedOnRenderThread { get; } = [];
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class CacheMutationThreadProbeDrawable(
    CacheMutationThreadState state) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawNode(new CacheMutationThreadProbeNode(state));
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(8, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class CacheMutationThreadProbeNode(
    CacheMutationThreadState state) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(new Color(255, 32, 64, 96)));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(CacheMutationThreadProbeNode),
            runtimeIdentity: new RenderRuntimeIdentity(typeof(CacheMutationThreadProbeNode)));
        context.Publish(context.OpaqueSource(description));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
            state.DisposedOnRenderThread.Add(RenderThread.Dispatcher.CheckAccess());
        base.OnDispose(disposing);
    }
}
