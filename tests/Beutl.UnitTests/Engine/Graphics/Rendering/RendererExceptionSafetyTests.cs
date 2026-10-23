using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Drives request-owner cleanup through the production frame renderer using recorded resources.
[NonParallelizable]
[TestFixture]
public class RendererExceptionSafetyTests
{
    [Test]
    public void RenderDrawable_DischargesFaultingAndUnexecutedResources_WhenExecutionThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var discharged = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16, RenderIntent.Preview);
            CompositionFrame frame = CreateFrame(
                discharged,
                new RecordedOperationSpec("first"),
                new RecordedOperationSpec("fault", ThrowOnExecute: true),
                new RecordedOperationSpec("remaining"));

            var ex = Assert.Throws<InvalidOperationException>(() => renderer.Render(frame));

            Assert.That(ex!.Message, Is.EqualTo("fault"));
            Assert.That(discharged, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
        });
    }

    [Test]
    public void RecalculateBoundaries_SurfacesMetadataCleanupFailure()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var discharged = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16, RenderIntent.Preview);
            CompositionFrame frame = CreateFrame(
                discharged,
                new RecordedOperationSpec("fault", ThrowOnDispose: true, TrackMetadataDischarge: true));
            renderer.UpdateFrame(frame);

            var ex = Assert.Throws<AggregateException>(() => renderer.RecalculateBoundaries(0));

            Assert.That(ex!.Flatten().InnerExceptions.Single().Message, Is.EqualTo("fault"));
            Assert.That(discharged, Is.EqualTo(new[] { "fault" }));
        });
    }

    [Test]
    public void RenderDrawable_ClearsCurrentFrameMetadata_WhenExecutionThrows()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = RendererTestSupport.CreateCpuRenderer(16, 16);
            var previous = new FaultingDrawable([new RecordedOperationSpec("previous")]);
            CompositionFrame previousFrame = RendererTestSupport.CreateFrame(new(16, 16), previous);
            renderer.Render(previousFrame);
            Assert.That(renderer.GetBoundary(previous), Is.Not.Null);

            var faulting = new FaultingDrawable([new RecordedOperationSpec("fault", ThrowOnExecute: true)]);
            CompositionFrame faultingFrame = RendererTestSupport.CreateFrame(new(16, 16), faulting);

            Assert.That(
                () => renderer.Render(faultingFrame),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("fault"));
            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundary(previous), Is.Null);
                Assert.That(renderer.GetBoundary(faulting), Is.Null);
                Assert.That(renderer.GetBoundaries(zIndex: 0), Is.Empty);
            });
        });
    }

    [Test]
    public void UpdateFrame_PublishesMetadataOnlyAfterEveryDrawableRecordsAndRetriesFaultedEntry()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = RendererTestSupport.CreateCpuRenderer(16, 16);
            var first = new SwitchableFaultingDrawable();
            var faulting = new SwitchableFaultingDrawable { ShouldFault = true };
            CompositionFrame frame = RendererTestSupport.CreateFrame(new(16, 16), first, faulting);

            Assert.That(
                () => renderer.UpdateFrame(frame),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("recording failed"));
            Assert.Multiple(() =>
            {
                Assert.That(renderer.GetBoundary(first), Is.Null);
                Assert.That(renderer.GetBoundary(faulting), Is.Null);
                Assert.That(renderer.GetBoundaries(zIndex: 0), Is.Empty);
            });

            faulting.ShouldFault = false;
            Assert.That(() => renderer.UpdateFrame(frame), Throws.Nothing);
            Assert.Multiple(() =>
            {
                // A faulted frame revalidates nothing, so the entry that already recorded keeps its mark
                // and re-records. Consuming it early would strand a mark on a node a skipped entry shares.
                Assert.That(first.RenderCalls, Is.EqualTo(2));
                Assert.That(faulting.RenderCalls, Is.EqualTo(2));
                Assert.That(renderer.GetBoundary(first), Is.Not.Null);
                Assert.That(renderer.GetBoundary(faulting), Is.Not.Null);
            });
        });
    }

    private static CompositionFrame CreateFrame(
        ICollection<string> discharged,
        params RecordedOperationSpec[] operations)
    {
        var drawable = new FaultingDrawable(operations) { Discharged = discharged };
        return RendererTestSupport.CreateFrame(new(16, 16), drawable) with
        {
            Eligibility = new CompositionEligibility([drawable]),
        };
    }
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class FaultingDrawable : Drawable
{
    private readonly RecordedOperationSpec[] _operations;

    public FaultingDrawable(RecordedOperationSpec[] operations)
    {
        _operations = operations;
    }

    public ICollection<string> Discharged { get; set; } = new List<string>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new FixedOpsNode(_operations, Discharged));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class FixedOpsNode : RenderNode
{
    private static readonly RenderResourceSlot<SolidColorBrush.Resource> s_fillSlot = new();
    private readonly IReadOnlyList<RecordedOperationSpec> _operations;
    private readonly ICollection<string> _discharged;

    public FixedOpsNode(
        IReadOnlyList<RecordedOperationSpec> operations,
        ICollection<string>? discharged = null)
    {
        _operations = operations;
        _discharged = discharged ?? new List<string>();
    }

    public override void Process(RenderNodeContext context)
    {
        RenderResource<SolidColorBrush.Resource> fillResource = context.Borrow(Brushes.Resource.White);
        for (int index = 0; index < _operations.Count; index++)
        {
            RecordedOperationSpec spec = _operations[index];
            bool trackDischarge = context.Purpose != RenderRequestPurpose.Bounds || spec.TrackMetadataDischarge;
            var operation = new RecordedOperation(spec, _discharged, trackDischarge);
            _ = context.Own(operation);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                spec,
                static (session, state) => session.UseResource(
                    s_fillSlot,
                    fill => RecordedOperation.Execute(state, session, fill)),
                OpaqueRenderBoundsContract.Source(spec.EffectiveBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [s_fillSlot.Bind(fillResource)]);
            context.Publish(context.OpaqueSource(description));
        }
    }
}

internal readonly record struct RecordedOperationSpec(
    string Name,
    bool ThrowOnExecute = false,
    bool AllocateBeforeThrow = false,
    bool ThrowOnDispose = false,
    string? DisposeFaultMessage = null,
    bool TrackMetadataDischarge = false)
{
    public Rect EffectiveBounds => new(0, 0, 4, 4);
}

internal sealed class RecordedOperation(
    RecordedOperationSpec spec,
    ICollection<string> discharged,
    bool trackDischarge) : IDisposable
{
    private bool _disposed;

    public static void Execute(
        RecordedOperationSpec spec,
        OpaqueRenderSession session,
        Brush.Resource fill)
    {
        if (spec.ThrowOnExecute && !spec.AllocateBeforeThrow)
            throw new InvalidOperationException(spec.Name);

        Rect bounds = spec.EffectiveBounds;
        using OpaqueRenderOutput output = session.CreateOutput(bounds);
        if (spec.ThrowOnExecute)
            throw new InvalidOperationException(spec.Name);

        output.Canvas.Use(canvas =>
            canvas.DrawRectangle(bounds, fill, pen: null));
        session.Publish(output);
    }

    public void Dispose()
    {
        if (_disposed)
            throw new InvalidOperationException($"{spec.Name}-double-dispose");

        _disposed = true;
        if (trackDischarge)
            discharged.Add(spec.Name);
        if (spec.ThrowOnDispose)
            throw new InvalidOperationException(spec.DisposeFaultMessage ?? spec.Name);
    }
}
