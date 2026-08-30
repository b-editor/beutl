using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Helpers;
using Beutl.Media;
using Beutl.Models;
using Beutl.Threading;

using Moq;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// An allocation has to fit what the device can attach, even though the plan it came from was clamped to
/// the engine's fixed ceiling.
/// </summary>
/// <remarks>
/// The budget is named rather than read from the machine running the suite, so the clamp is observable on a
/// device whose own limit is the engine ceiling and the expectations do not move between devices.
/// </remarks>
[TestFixture]
public sealed class DeviceBufferBudgetTests
{
    private const int DeviceBudget = 8192;

    // 10000 px at w 2 is 20000 device px: over both the engine ceiling and the named budget, and clamped to
    // a different density by each.
    private static readonly Rect s_overBudgetBounds = new(0, 0, 10000, 1);

    // Over the named budget and under the engine ceiling, so planning leaves the density alone at output
    // scale 1 and only the device budget can refuse the buffer.
    private static readonly Rect s_overBudgetDomain = new(0, 0, 10000, 8);

    [Test]
    public void ClampToDeviceBudget_FitsTheNamedBudgetRatherThanTheEngineCeiling()
    {
        float engineFit = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(s_overBudgetBounds, 2f);
        float deviceFit = RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
            s_overBudgetBounds,
            2f,
            DeviceBudget);

        Assert.Multiple(() =>
        {
            Assert.That(
                PixelRect.FromRect(s_overBudgetBounds, engineFit).Width,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "the fixture must reach the engine ceiling, or the two budgets are indistinguishable");
            Assert.That(deviceFit, Is.LessThan(engineFit));
            Assert.That(
                PixelRect.FromRect(s_overBudgetBounds, deviceFit).Width,
                Is.LessThanOrEqualTo(DeviceBudget));
        });
    }

    [Test]
    public void ClampToDeviceBudget_WithoutANamedBudgetUsesTheResolvedDeviceLimit()
    {
        int resolved = RenderScaleUtilities.ResolveMaxBufferDimension();

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(s_overBudgetBounds, 2f),
                Is.EqualTo(RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
                    s_overBudgetBounds,
                    2f,
                    resolved)));
        });
    }

    [Test]
    public void ResolveMaxBufferDimension_AnswersForTheContextItIsAskedAbout()
    {
        // GraphicsContextFactory.Shutdown is public, so the context that first answered can be replaced by
        // one that attaches less - and a limit remembered from the first would then ask that device for an
        // attachment it cannot make.
        IGraphicsContext smaller = ContextAttaching(DeviceBudget / 2);
        IGraphicsContext larger = ContextAttaching(DeviceBudget);

        Assert.Multiple(() =>
        {
            Assert.That(RenderScaleUtilities.ResolveMaxBufferDimension(larger), Is.EqualTo(DeviceBudget));
            Assert.That(
                RenderScaleUtilities.ResolveMaxBufferDimension(smaller),
                Is.EqualTo(DeviceBudget / 2),
                "a device that attaches less must not inherit the previous context's limit");
            Assert.That(
                RenderScaleUtilities.ResolveMaxBufferDimension(larger),
                Is.EqualTo(DeviceBudget),
                "and a device that attaches more must not stay capped by it");
            Assert.That(
                RenderScaleUtilities.ResolveMaxBufferDimension(null),
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "with no context the engine ceiling is a placeholder, not a remembered answer");
        });
    }

    [Test]
    public void ResolveMaxBufferDimension_NeverExceedsTheEngineCeiling()
    {
        int resolved = RenderScaleUtilities.ResolveMaxBufferDimension(
            ContextAttaching(RenderScaleUtilities.MaxBufferDimension * 2));

        Assert.That(resolved, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
    }

    [Test]
    public void ResolveTargetDensity_FitsTheContextsBufferDimension()
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            workingScale: 2f,
            maxBufferDimension: DeviceBudget);

        float density = context.ResolveTargetDensity(s_overBudgetBounds);

        Assert.Multiple(() =>
        {
            Assert.That(context.MaxBufferDimension, Is.EqualTo(DeviceBudget));
            Assert.That(
                CustomFilterEffectContext.DeviceBufferSize(s_overBudgetBounds, density).Width,
                Is.LessThanOrEqualTo(DeviceBudget));
            Assert.That(
                density,
                Is.EqualTo(RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
                    new Rect(default, s_overBudgetBounds.Size),
                    2f,
                    DeviceBudget)));
        });
    }

    [Test]
    public void CreateTarget_AllocatesWithinTheBudget_AndReportsTheDensityItAllocated()
    {
        var factory = new RecordingCpuTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Delivery);
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            workingScale: 2f,
            renderTargetLeaseSession: session,
            maxBufferDimension: DeviceBudget);

        using EffectTarget target = context.CreateTarget(s_overBudgetBounds);

        Assert.Multiple(() =>
        {
            Assert.That(target.IsEmpty, Is.False,
                "an over-budget request must degrade density, not drop the target");
            Assert.That(factory.Requests, Has.Count.EqualTo(1));
            Assert.That(factory.Requests[0].Width, Is.LessThanOrEqualTo(DeviceBudget));
            Assert.That(target.RenderTarget!.Width, Is.EqualTo(factory.Requests[0].Width));
            Assert.That(
                PixelRect.FromRect(s_overBudgetBounds, target.Scale.Value).Width,
                Is.EqualTo(factory.Requests[0].Width),
                "the density read back from the target must describe the buffer that was allocated");
        });
    }

    [Test]
    public void MaxBufferDimension_DefaultsToWhatTheActiveDeviceCanAttach()
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null);

        Assert.Multiple(() =>
        {
            Assert.That(
                context.MaxBufferDimension,
                Is.EqualTo(RenderScaleUtilities.ResolveMaxBufferDimension()));
            Assert.That(
                activator.MaxBufferDimension,
                Is.EqualTo(RenderScaleUtilities.ResolveMaxBufferDimension()));
        });
    }

    [Test]
    public void ANonPositiveBufferDimensionIsRejected()
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => new CustomFilterEffectContext(
                    targets,
                    RenderIntent.Delivery,
                    RenderRequestPurpose.Auxiliary,
                    maxBufferDimension: 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new FilterEffectActivator(
                    targets,
                    builder,
                    RenderIntent.Delivery,
                    RenderRequestPurpose.Auxiliary,
                    outputScale: 1f,
                    workingScale: 1f,
                    maxWorkingScale: float.PositiveInfinity,
                    deviceGridOffset: default,
                    maxBufferDimension: -1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void FitsBufferBudget_MeasuresBothAxesAgainstTheNamedBudget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderScaleUtilities.FitsBufferBudget(new PixelSize(DeviceBudget, DeviceBudget), DeviceBudget), Is.True);
            Assert.That(RenderScaleUtilities.FitsBufferBudget(new PixelSize(DeviceBudget + 1, 1), DeviceBudget), Is.False);
            Assert.That(RenderScaleUtilities.FitsBufferBudget(new PixelSize(1, DeviceBudget + 1), DeviceBudget), Is.False);
            Assert.That(
                RenderScaleUtilities.FitsBufferBudget(new PixelSize(RenderScaleUtilities.MaxBufferDimension, 1)),
                Is.EqualTo(RenderScaleUtilities.ResolveMaxBufferDimension() >= RenderScaleUtilities.MaxBufferDimension),
                "without a named budget the active device's limit decides");
            Assert.That(
                () => RenderScaleUtilities.FitsBufferBudget(new PixelSize(1, 1), 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void AnOverBudgetLease_IsDeclinedBeforeItReachesTheAllocator()
    {
        var factory = new RecordingCpuTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory, maxBufferDimension: DeviceBudget);
        using RenderTargetLeaseSession preview = registry.BeginSession(RenderIntent.Preview);

        var overBudget = new PixelSize(DeviceBudget + 1, 1);
        RenderTargetLease? declined = preview.TryAcquire(overBudget);
        using RenderTargetLease atBudget = preview.Acquire(new PixelSize(DeviceBudget, 1));

        Assert.Multiple(() =>
        {
            Assert.That(declined, Is.Null, "a preview drops the target it cannot attach");
            Assert.That(
                () => preview.Acquire(overBudget),
                Throws.InstanceOf<InvalidOperationException>()
                    .With.Message.Contains(DeviceBudget.ToString()));
            Assert.That(atBudget.Target.Width, Is.EqualTo(DeviceBudget));
            Assert.That(
                factory.Requests,
                Has.None.Matches<PixelSize>(size => size.Width > DeviceBudget || size.Height > DeviceBudget),
                "an attachment the device cannot make must never reach the allocator");
        });
    }

    [Test]
    public void AnOverBudgetLease_FailsADeliveryRenderNamingTheLimit()
    {
        var factory = new RecordingCpuTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory, maxBufferDimension: DeviceBudget);
        using RenderTargetLeaseSession delivery = registry.BeginSession(RenderIntent.Delivery);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => delivery.TryAcquire(new PixelSize(1, DeviceBudget + 1)),
                Throws.InstanceOf<InvalidOperationException>()
                    .With.Message.Contains(DeviceBudget.ToString()),
                "a delivery render never degrades");
            Assert.That(factory.Requests, Is.Empty);
        });
    }

    /// <remarks>
    /// The materialization is not one the plan marked droppable under allocation pressure. A device-budget
    /// refusal is not pressure - no allocator this session can reach will ever attach the buffer - so the
    /// render intent decides it, and a preview gives up the contribution rather than failing the frame.
    /// Completing at all is what proves the drop was recorded rather than swallowed: the island holding the
    /// refused materialization is skipped and is not region-empty, and the execution ledger accepts a
    /// skipped island only once PreviewAllocationDropObserved is set - the same flag that keeps
    /// StageCacheCaptures and the backdrop publication from carrying the incomplete frame forward.
    /// </remarks>
    [Test]
    public void AnOverBudgetMaterialization_DropsThePreviewContribution_WithoutAskingTheAllocator()
    {
        using var node = new OverBudgetSourceNode();
        var factory = new RecordingCpuTargetFactory();
        using RenderRequest request = CreateOverBudgetRequest(RenderIntent.Preview);
        using CompiledRenderRequest compiled = CompileOverBudgetRequest(request, node);
        using RenderTarget destination = new CpuRenderTarget(new PixelSize(8, 8));
        using var canvas = new ImmediateCanvas(destination, RenderIntent.Preview);
        using var registry = new RenderTargetLeaseRegistry(factory, maxBufferDimension: DeviceBudget);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview, destination);

        Assert.DoesNotThrow(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas),
            "a preview drops the contribution, and records the drop so the frame is not treated as whole");

        Assert.Multiple(() =>
        {
            Assert.That(
                node.ExecuteCalls,
                Is.GreaterThan(0),
                "the fixture must reach the materialization the budget then refuses");
            Assert.That(
                factory.Requests,
                Has.None.Matches<PixelSize>(size => size.Width > DeviceBudget || size.Height > DeviceBudget),
                "an ordinary materialization must not ask for an over-budget attachment");
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void AnOverBudgetMaterialization_StillFailsADeliveryRenderNamingTheLimit()
    {
        using var node = new OverBudgetSourceNode();
        var factory = new RecordingCpuTargetFactory();
        using RenderRequest request = CreateOverBudgetRequest(RenderIntent.Delivery);
        using CompiledRenderRequest compiled = CompileOverBudgetRequest(request, node);
        using RenderTarget destination = new CpuRenderTarget(new PixelSize(8, 8));
        using var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery);
        using var registry = new RenderTargetLeaseRegistry(factory, maxBufferDimension: DeviceBudget);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Delivery, destination);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => new RenderRequestExecutor(targets).Execute(compiled, canvas));

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal!.Message,
                Does.Contain(DeviceBudget.ToString()),
                "an export must fail rather than deliver a frame missing the content it could not attach");
            Assert.That(
                factory.Requests,
                Has.None.Matches<PixelSize>(size => size.Width > DeviceBudget || size.Height > DeviceBudget));
            Assert.That(registry.Statistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void ACallerSuppliedAllocator_IsMeasuredAgainstTheEngineCeiling_NotTheBoundDevicesLimit()
    {
        // Both pools are bound to the same device, so which budget each one gets can only follow from which
        // allocator fills its requests. The probes run on the render dispatcher, the only place the pool's
        // own allocator attaches through that device at all - off it, it rasters and both answer the ceiling.
        IGraphicsContext device = ContextAttaching(DeviceBudget);
        var atTheEngineCeiling = new PixelSize(RenderScaleUtilities.MaxBufferDimension, 2);
        var factory = new RecordingCpuTargetFactory();
        using var callerAllocated = new RenderTargetPool(factory);
        using RenderTargetPoolRequest callerRequest = callerAllocated.BeginImplicitRequest(device);
        using var poolAllocated = new RenderTargetPool(factory: null);
        using RenderTargetPoolRequest poolRequest = poolAllocated.BeginImplicitRequest(device);

        (bool callerRefuses, int callerBudget, bool poolRefuses, int poolBudget) =
            RenderThread.Dispatcher.Invoke(() =>
            {
                bool callerRefused = callerAllocated.ExceedsBufferBudget(
                    callerRequest,
                    atTheEngineCeiling,
                    out int callerMax);
                bool poolRefused = poolAllocated.ExceedsBufferBudget(
                    poolRequest,
                    atTheEngineCeiling,
                    out int poolMax);
                return (callerRefused, callerMax, poolRefused, poolMax);
            });

        using PooledRenderTargetLease lease = callerRequest.Acquire(atTheEngineCeiling);

        Assert.Multiple(() =>
        {
            Assert.That(callerBudget, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(callerRefuses, Is.False);
            Assert.That(
                factory.Requests,
                Is.EqualTo(new[] { atTheEngineCeiling }),
                "an allocator that never attaches through the shared context must be asked for what it can make");
            Assert.That(lease.Target.Width, Is.EqualTo(atTheEngineCeiling.Width));
            Assert.That(
                poolBudget,
                Is.EqualTo(DeviceBudget),
                "the fixture must name a device below the engine ceiling, or the two budgets are indistinguishable");
            Assert.That(poolRefuses, Is.True);
        });
    }

    [Test]
    public void TheEnginesOwnAllocator_IsStillRefusedPastTheBoundDevicesLimit()
    {
        var pastTheDevice = new PixelSize(DeviceBudget + 1, 1);
        using var pool = new RenderTargetPool(factory: null);
        using RenderTargetPoolRequest request = pool.BeginImplicitRequest(ContextAttaching(DeviceBudget));

        // On the render dispatcher the pool's own allocator does attach through a shared context, so the
        // named device's limit is the one that binds - and it has to bind before the allocator is reached,
        // because an attachment the device cannot make is undefined behaviour rather than a failed
        // allocation. Nothing here allocates, so no real context is ever created to observe that.
        (int budget, bool refuses, bool acquired, PooledRenderTargetLease? declined, Exception? refusal) =
            RenderThread.Dispatcher.Invoke(() =>
            {
                bool refused = pool.ExceedsBufferBudget(request, pastTheDevice, out int resolved);
                bool got = pool.TryAcquire(request, pastTheDevice, out PooledRenderTargetLease? lease);
                Exception? thrown = null;
                try
                {
                    pool.Acquire(request, new PixelSize(1, DeviceBudget + 1)).Dispose();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                return (resolved, refused, got, lease, thrown);
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                budget,
                Is.EqualTo(DeviceBudget),
                "the fixture must name a device below the engine ceiling, or the two budgets are indistinguishable");
            Assert.That(refuses, Is.True);
            Assert.That(acquired, Is.False, "the pool attaches this one itself, so the device's limit binds it");
            Assert.That(declined, Is.Null);
            Assert.That(
                refusal,
                Is.InstanceOf<InvalidOperationException>().With.Message.Contains(DeviceBudget.ToString()));
            Assert.That(
                pool.Statistics.Creates,
                Is.Zero,
                "an attachment the bound device cannot make must never reach the allocator");
        });
    }

    [Test]
    public void TheAllocationPathDecidesTheBudget_NotWhicheverContextIsInstalled()
    {
        IGraphicsContext device = ContextAttaching(DeviceBudget);

        // The one decision both the allocation and its budget read. Off a dispatcher RenderTarget.Create
        // rasters, so the installed context bounds nothing; on one it attaches, so the device's limit binds.
        IGraphicsContext? offDispatcher = RenderTarget.ResolveCreationContext(device);
        IGraphicsContext? onDispatcher = RenderThread.Dispatcher.Invoke(
            () => RenderTarget.ResolveCreationContext(device));

        Assert.Multiple(() =>
        {
            Assert.That(Dispatcher.Current, Is.Null, "the fixture must be off the render dispatcher");
            Assert.That(offDispatcher, Is.Null);
            Assert.That(onDispatcher, Is.SameAs(device));
            Assert.That(
                RenderScaleUtilities.ResolveMaxBufferDimension(offDispatcher),
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                RenderScaleUtilities.ResolveMaxBufferDimension(onDispatcher),
                Is.EqualTo(DeviceBudget));
        });
    }

    [Test]
    public void AnOffDispatcherRender_IsBudgetedAgainstTheCpuRasterItAllocates()
    {
        // Nothing here runs on a dispatcher, so RenderTarget.Create rasters on the CPU whatever context the
        // request names, and no device's attachment limit bounds what it allocates. The limit is the named
        // mock's rather than the running machine's, so the sub-ceiling device is forced on any GPU.
        Assert.That(Dispatcher.Current, Is.Null, "the case only arises off a dispatcher");
        var pastTheDevice = new PixelSize(DeviceBudget + 1, 1);
        using var pool = new RenderTargetPool(factory: null);
        using RenderTargetPoolRequest request = pool.BeginImplicitRequest(ContextAttaching(DeviceBudget));

        bool refuses = pool.ExceedsBufferBudget(request, pastTheDevice, out int budget);
        bool acquired = pool.TryAcquire(request, pastTheDevice, out PooledRenderTargetLease? lease);

        using (lease)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    budget,
                    Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                    "a CPU raster answers to the engine ceiling, not to a device it never reaches");
                Assert.That(refuses, Is.False);
                Assert.That(acquired, Is.True, "a valid CPU render must not be refused before it allocates");
                Assert.That(lease?.Target.Width, Is.EqualTo(pastTheDevice.Width));
            });
        }
    }

    [Test]
    public void ACallerSuppliedAllocatorThatDeclines_SurfacesAsADeclineNotABudgetRefusal()
    {
        // Within the engine ceiling and past what a sub-ceiling device could attach, so the allocator's own
        // refusal is the only one left to report.
        var declined = new PixelSize(RenderScaleUtilities.MaxBufferDimension, 1);
        var factory = new DecliningTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession delivery = registry.BeginSession(RenderIntent.Delivery);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => delivery.Acquire(declined));

        Assert.Multiple(() =>
        {
            Assert.That(
                factory.Calls,
                Is.EqualTo(1),
                "the allocator decides what it can make; a device budget must not pre-empt it");
            Assert.That(refusal!.Message, Does.Contain("could not allocate"));
            Assert.That(refusal.Message, Does.Not.Contain("can attach"));
        });
    }

    /// <summary>
    /// A request opened before any GPU work has happened pins no context, because there is none installed to
    /// pin. The allocation behind it still builds one, so the budget has to ask for the device that
    /// allocation will get rather than fall back to the engine ceiling.
    /// </summary>
    [Test]
    public void ABudgetTakenBeforeTheFirstDevice_MeasuresAgainstTheDeviceTheAllocationWillBuild()
    {
        var pastTheDevice = new PixelSize(DeviceBudget + 1, 1);
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        (bool pinnedNothing, bool refuses, int budget) = WithAllocationDevice(device.Object, () =>
            RenderThread.Dispatcher.Invoke(() =>
            {
                // Nothing installed is the state this case is about, and the suite cannot assume it: any
                // earlier test may have left a real device behind. Standing in for the installed state is
                // what makes the fixture reach the un-pinned branch on every machine.
                InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                    new InstalledGraphics(null, null, null, FailedToInitialize: false));
                try
                {
                    using var pool = new RenderTargetPool(factory: null);
                    using RenderTargetPoolRequest request = pool.BeginRequest();
                    bool absent = GraphicsContextFactory.SharedContext is null;
                    bool refused = pool.ExceedsBufferBudget(request, pastTheDevice, out int resolved);
                    return (absent, refused, resolved);
                }
                finally
                {
                    GraphicsContextFactory.ExchangeInstalledGraphics(live);
                }
            }));

        Assert.Multiple(() =>
        {
            Assert.That(pinnedNothing, Is.True, "the fixture must open the request with no context installed");
            Assert.That(
                budget,
                Is.EqualTo(DeviceBudget),
                "the engine ceiling here would let through an attachment the device about to be built cannot make");
            Assert.That(refuses, Is.True);
            AssertNeverAttached(device);
        });
    }

    /// <summary>
    /// The root output surface is <c>ceil(FrameSize * s_out)</c> and is allocated directly rather than
    /// through the pool, so nothing else can bound it: it has to refuse for itself, naming the same limit a
    /// pooled refusal names.
    /// </summary>
    [Test]
    public void ARootSurfacePastTheDevicesLimit_IsRefusedBeforeItReachesTheAllocator()
    {
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        Exception? refusal = WithAllocationDevice(device.Object, () =>
        {
            try
            {
                // Within the engine ceiling and past what this device can attach, so only the device's own
                // limit is left to refuse it.
                using var renderer = new Renderer(DeviceBudget + 1, 1, RenderIntent.Delivery);
                return null;
            }
            catch (Exception ex)
            {
                // The construction runs through Dispatcher.Invoke, which reports whatever it caught as the
                // aggregate of a faulted task.
                return ex is AggregateException aggregate ? aggregate.Flatten().InnerException : ex;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal,
                Is.InstanceOf<InvalidOperationException>()
                    .With.Message.Contains(DeviceBudget.ToString()),
                "the root surface must report the limit it could not fit, as a pooled refusal does");
            AssertNeverAttached(device);
        });
    }

    /// <summary>
    /// The render-target factory is public and runs wherever a plugin's filter effect, drawable or render
    /// node does, so an extent past what the device can attach has to be refused inside it rather than by
    /// whichever caller happened to route through it.
    /// </summary>
    /// <remarks>
    /// A driver does not report that attachment as a failure: SwiftShader builds a framebuffer wider than
    /// its own limit and returns success, MoltenVK aborts the process on a Metal assertion. Neither reaches
    /// the factory's catch, so the target coming back null proves nothing on its own - the assertion is
    /// that the allocator was never asked.
    /// </remarks>
    [Test]
    public void ADirectCreatePastTheDevicesLimit_IsRefusedBeforeItReachesTheAllocator()
    {
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        // Within the engine ceiling and past what this device can attach, so only the device's own limit is
        // left to refuse it. The dispatcher is what makes the factory attach rather than raster.
        RenderTarget? created = WithAllocationDevice(
            device.Object,
            () => RenderThread.Dispatcher.Invoke(() => RenderTarget.Create(DeviceBudget + 1, 1)));
        created?.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Null, "a refused allocation must not hand back a target");
            AssertNeverAttached(device);
        });
    }

    /// <summary>
    /// The negative control for the refusal above: off the render dispatcher the same call rasters on the
    /// CPU, which no device's attachment limit bounds, so it has to keep allocating the extent it always
    /// did.
    /// </summary>
    [Test]
    public void ADirectCreateOffTheDispatcher_IsStillAllocatedPastTheDevicesLimit()
    {
        Assert.That(Dispatcher.Current, Is.Null, "the case only arises off a dispatcher");
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        RenderTarget? created = WithAllocationDevice(
            device.Object,
            () => RenderTarget.Create(DeviceBudget + 1, 1));

        using (created)
        {
            Assert.Multiple(() =>
            {
                Assert.That(created, Is.Not.Null, "a CPU raster answers to no device's attachment limit");
                Assert.That(created!.Width, Is.EqualTo(DeviceBudget + 1));
                AssertNeverAttached(device);
            });
        }
    }

    /// <summary>
    /// The export and save-frame dialogs pre-validate the root surface before any rendering starts. Measuring
    /// that against the engine ceiling admits a size the device then refuses, so the user is told the export
    /// is fine and the render fails afterwards.
    /// </summary>
    [Test]
    public void RootSurfacePreValidation_FollowsTheDeviceRatherThanTheEngineCeiling()
    {
        // 4K at 4x is 15360x8640: inside the engine ceiling, past a device that tops out at 8192.
        var frame = new PixelSize(3840, 2160);
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        (bool supersampleFitsCeiling,
            bool supersampleFitsDevice,
            bool saveFrameFitsCeiling,
            bool saveFrameFitsDevice) = WithInstalledDevice(device.Object, () =>
            RenderThread.Dispatcher.Invoke(() => (
                ExportSupersampling.FitsBufferLimit(frame, 4, RenderScaleUtilities.MaxBufferDimension),
                ExportSupersampling.FitsBufferLimit(frame, 4),
                SaveFrameScale.FitsBufferLimit(frame, 4f, RenderScaleUtilities.MaxBufferDimension),
                SaveFrameScale.FitsBufferLimit(frame, 4f))));

        Assert.Multiple(() =>
        {
            Assert.That(
                supersampleFitsCeiling,
                Is.True,
                "the fixture must clear the engine ceiling, or the two budgets are indistinguishable");
            Assert.That(saveFrameFitsCeiling, Is.True);
            Assert.That(supersampleFitsDevice, Is.False);
            Assert.That(saveFrameFitsDevice, Is.False);
        });
    }

    /// <summary>
    /// Both dialogs evaluate their warning on the Avalonia UI thread, never on the render dispatcher, so the
    /// pre-validation has to reach the device from off it.
    /// </summary>
    [Test]
    public void RootSurfacePreValidation_FollowsTheDeviceFromOffTheRenderDispatcher()
    {
        // 4K at 4x is 15360x8640: inside the engine ceiling, past a device that tops out at 8192.
        var frame = new PixelSize(3840, 2160);
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        (int allocationLimit, int predicted, bool supersampleFits, bool saveFrameFits) =
            WithInstalledDevice(device.Object, () => (
                RenderScaleUtilities.ResolveMaxBufferDimension(),
                RenderScaleUtilities.PredictRenderThreadMaxBufferDimension(),
                ExportSupersampling.FitsBufferLimit(frame, 4),
                SaveFrameScale.FitsBufferLimit(frame, 4f)));

        Assert.Multiple(() =>
        {
            Assert.That(Dispatcher.Current, Is.Null, "the fixture must ask from where the dialogs ask");
            Assert.That(
                allocationLimit,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "a buffer allocated here is rastered on the CPU, so the device must still not bound it");
            Assert.That(
                predicted,
                Is.EqualTo(DeviceBudget),
                "what the render thread will resolve is a different question, and the device answers it");
            Assert.That(supersampleFits, Is.False);
            Assert.That(saveFrameFits, Is.False);
            AssertNeverAttached(device);
        });
    }

    /// <summary>
    /// The prediction is only worth taking because it is the render thread's own answer. Both are read for
    /// the same installed device, so nothing but the thread they are asked from can separate them.
    /// </summary>
    [Test]
    public void PredictRenderThreadMaxBufferDimension_AnswersWhatTheRenderThreadResolves()
    {
        Mock<IGraphicsContext> device = MockAttaching(DeviceBudget);

        (int predictedOffIt, int resolvedOnIt) = WithInstalledDevice(device.Object, () => (
            RenderScaleUtilities.PredictRenderThreadMaxBufferDimension(),
            RenderThread.Dispatcher.Invoke(RenderScaleUtilities.ResolveMaxBufferDimension)));

        Assert.Multiple(() =>
        {
            Assert.That(Dispatcher.Current, Is.Null, "the prediction must be taken from off the render thread");
            Assert.That(resolvedOnIt, Is.EqualTo(DeviceBudget));
            Assert.That(predictedOffIt, Is.EqualTo(resolvedOnIt));
            AssertNeverAttached(device);
        });
    }

    /// <summary>
    /// Building a device is render-thread-only, so a predicting caller can only read one that is already
    /// installed. With none there is nothing to measure, and the engine ceiling is the bound every
    /// measurement satisfies rather than a device's answer standing in for another's.
    /// </summary>
    [Test]
    public void PredictRenderThreadMaxBufferDimension_BoundsAnUnbuiltDeviceByTheEngineCeiling()
    {
        Mock<IGraphicsContext> pastTheCeiling = MockAttaching(RenderScaleUtilities.MaxBufferDimension * 2);

        int withoutADevice = WithInstalledDevice(
            device: null,
            RenderScaleUtilities.PredictRenderThreadMaxBufferDimension);
        int withARoomierDevice = WithInstalledDevice(
            pastTheCeiling.Object,
            RenderScaleUtilities.PredictRenderThreadMaxBufferDimension);

        Assert.Multiple(() =>
        {
            Assert.That(
                withoutADevice,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "nothing is built to measure, so the answer is the bound every measurement satisfies");
            Assert.That(
                withARoomierDevice,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension),
                "and a device that attaches more is still held to the engine's own ceiling");
            AssertNeverAttached(pastTheCeiling);
        });
    }

    /// <summary>
    /// An effect item allocates at what the device can attach, which on a device below the engine ceiling is
    /// less than the density the plan - and therefore a cache key - was built from. Nothing may be stored
    /// under a key whose pixels do not exist at that density, so such a segment must not be a cache
    /// candidate at all.
    /// </summary>
    /// <remarks>
    /// The clamped density is named here rather than read from the machine running the suite, so the case is
    /// reached on a device whose own limit is the engine ceiling.
    /// </remarks>
    [Test]
    public void ADeviceClampedEffectItem_IsNeverKeyedOnThePlannedDensity()
    {
        float plannedDensity = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
            s_overBudgetDomain,
            1f);
        var effect = new DeviceClampedCustomEffect();
        var effectNode = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), s_overBudgetDomain),
            effectNode);
        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
            RenderNodeCacheHelper.BeginLifecycle(effectNode).CompleteSuccessfully(advanceWarmup: true);

        RenderCacheDecision[] segmentDecisions = ResolveEffectSegmentDecisions(pipeline);
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = RenderCacheOptions.Enabled,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    TargetDomain = s_overBudgetDomain,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new RecordingCpuTargetFactory(),
            });

        using RenderNodeRasterization first = renderer.Rasterize();
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(plannedDensity, Is.EqualTo(1f),
                "the fixture must plan at a density the engine ceiling leaves alone");
            Assert.That(DeviceClampedCustomEffect.LastPublishedDensity, Is.LessThan(plannedDensity),
                "the fixture must publish a target the device clamped below the planned density");
            Assert.That(segmentDecisions, Is.Not.Empty,
                "the fixture must reach the cache candidate whose key the clamped density would break");
            Assert.That(
                segmentDecisions,
                Has.All.Property(nameof(RenderCacheDecision.BypassReason))
                    .EqualTo(RenderCacheBypassReason.RawTargetWork));
            Assert.That(first.IsEmpty, Is.False);
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(effectNode.Cache.IsCached, Is.False,
                "pixels that exist only at the clamped density must not be stored under the planned key");
            Assert.That(
                second.Bitmap!.GetPixelSpan().SequenceEqual(first.Bitmap!.GetPixelSpan()),
                Is.True,
                "the repeated frame must recompute the segment rather than reuse a mismatched entry");
        });
    }

    /// <summary>
    /// Every cache decision covering a filter-effect segment that materializes through the effect-item path.
    /// </summary>
    private static RenderCacheDecision[] ResolveEffectSegmentDecisions(RenderNode node)
    {
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_overBudgetDomain,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Enabled));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler(
            renderCacheContext: new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                new RenderCacheDeviceContextIdentity("device", "context")));
        using CompiledRenderRequest compiled = compiler.Compile(request, graph);
        Dictionary<RenderFragmentId, RenderFragmentReference> references = graph.Fragments.ToDictionary(
            static fragment => fragment.Id,
            static fragment => (RenderFragmentReference)fragment.Payload!);
        return [.. compiled.CacheResolution.Decisions.Where(decision =>
            references.TryGetValue(decision.Candidate.FragmentId, out RenderFragmentReference? reference)
            && reference.Kind == RenderFragmentKind.FilterEffectSegment
            && !FilterEffectSegmentDirectReplaySupport.CanMaterialize(reference))];
    }
    private static RenderRequest CreateOverBudgetRequest(RenderIntent intent)
        => new(new RenderRequestOptions(
            intent,
            RenderRequestPurpose.Frame,
            targetDomain: s_overBudgetDomain,
            requestedRegion: s_overBudgetDomain,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled));

    private static CompiledRenderRequest CompileOverBudgetRequest(RenderRequest request, RenderNode node)
        => new RenderRequestCompiler().Compile(request, new RenderRequestRecorder(request).Record(node));

    private static IGraphicsContext ContextAttaching(int maxAttachmentDimension)
        => MockAttaching(maxAttachmentDimension).Object;

    private static Mock<IGraphicsContext> MockAttaching(int maxAttachmentDimension)
    {
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.SetupGet(c => c.MaxAttachmentDimension).Returns(maxAttachmentDimension);
        return context;
    }

    /// <summary>Runs <paramref name="body"/> with <paramref name="device"/> installed as the shared context.</summary>
    /// <remarks>
    /// A caller that cannot build a device can only read one that is already installed, so this is the state
    /// such a caller sees - and standing in for it is what forces a sub-ceiling device on any GPU.
    /// </remarks>
    private static T WithInstalledDevice<T>(IGraphicsContext? device, Func<T> body)
    {
        InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(
            new InstalledGraphics(device, null, null, FailedToInitialize: false));
        try
        {
            return body();
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    /// <summary>Runs <paramref name="body"/> with <paramref name="device"/> as the context an allocation builds.</summary>
    private static T WithAllocationDevice<T>(IGraphicsContext device, Func<T> body)
    {
        Func<IGraphicsContext?> previous = RenderTarget.ExchangeAllocationContext(() => device);
        try
        {
            return body();
        }
        finally
        {
            RenderTarget.ExchangeAllocationContext(previous);
        }
    }

    private static void AssertNeverAttached(Mock<IGraphicsContext> device)
        => device.Verify(
            c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()),
            Times.Never,
            "an attachment the device cannot make must never reach the allocator");

    private sealed class OverBudgetSourceNode : RenderNode
    {
        private static readonly OpaqueRenderDefinition<Action<OpaqueRenderSession>> s_definition =
            OpaqueRenderDefinition<Action<OpaqueRenderSession>>.Create(
                static (session, execute) => execute(session),
                OpaqueRenderBoundsContract.Source(s_overBudgetDomain),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);

        public int ExecuteCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(s_definition.Call(session =>
            {
                ExecuteCalls++;
                using OpaqueRenderOutput output = session.CreateOutput(s_overBudgetDomain);
                output.Canvas.Use(canvas => canvas.Clear(Color.FromArgb(255, 100, 149, 237)));
                session.Publish(output);
            })));
        }
    }

    private sealed class DecliningTargetFactory : IRenderTargetFactory
    {
        public int Calls { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Calls++;
            return null;
        }
    }

    private sealed class RecordingCpuTargetFactory : IRenderTargetFactory
    {
        public List<PixelSize> Requests { get; } = [];

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            Requests.Add(allocation.DeviceSize);
            return new CpuRenderTarget(allocation.DeviceSize);
        }
    }

    /// <summary>
    /// An effect item that publishes at the density a device attaching <see cref="DeviceBudget"/> would have
    /// allocated, which is what <c>CustomFilterEffectContext.CreateTarget</c> hands back on such a device.
    /// </summary>
    [SuppressResourceClassGeneration]
    private sealed partial class DeviceClampedCustomEffect : FilterEffect
    {
        public static float LastPublishedDensity { get; private set; }

        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                0,
                static (_, execution) => execution.ForEach((_, source) =>
                {
                    float density = RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
                        new Rect(default, source.Bounds.Size),
                        execution.WorkingScale,
                        DeviceBudget);
                    (int width, int height) = CustomFilterEffectContext.DeviceBufferSize(
                        source.Bounds,
                        density);
                    using RenderTarget backing = new CpuRenderTarget(new PixelSize(width, height));
                    var replacement = new EffectTarget(
                        backing,
                        source.Bounds,
                        EffectiveScale.At(density));
                    using (ImmediateCanvas canvas = execution.Open(replacement))
                    {
                        canvas.Clear();
                        source.Draw(canvas);
                    }

                    LastPublishedDensity = density;
                    return replacement;
                }),
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed class CpuRenderTarget(PixelSize size)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create a CPU device-budget test surface."),
            size.Width,
            size.Height);
}
