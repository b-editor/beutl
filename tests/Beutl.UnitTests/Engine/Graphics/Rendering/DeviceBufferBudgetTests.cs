using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
            RenderRequestPurpose.Auxiliary);

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

    private static IGraphicsContext ContextAttaching(int maxAttachmentDimension)
    {
        var context = new Mock<IGraphicsContext>(MockBehavior.Strict);
        context.SetupGet(c => c.MaxAttachmentDimension).Returns(maxAttachmentDimension);
        return context.Object;
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
