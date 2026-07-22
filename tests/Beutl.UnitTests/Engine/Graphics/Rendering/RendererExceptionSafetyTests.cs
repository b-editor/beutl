using System.Collections.Immutable;

using Beutl.Composition;
using Beutl.Engine;
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
            using var renderer = new Renderer(16, 16);
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
    public void RecalculateBoundaries_DischargesEveryMetadataResource_WhenOneDischargeThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var discharged = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16);
            CompositionFrame frame = CreateFrame(
                discharged,
                new RecordedOperationSpec("first", TrackMetadataDischarge: true),
                new RecordedOperationSpec("fault", ThrowOnDispose: true, TrackMetadataDischarge: true),
                new RecordedOperationSpec("remaining", TrackMetadataDischarge: true));
            renderer.UpdateFrame(frame);

            var ex = Assert.Throws<AggregateException>(() => renderer.RecalculateBoundaries(0));

            // Metadata-only requests own the same recording resources. Cleanup is strict LIFO and a
            // failing resource cannot stop later cleanup or cause the same resource to run twice.
            Assert.That(ex!.Flatten().InnerExceptions.Single().Message, Is.EqualTo("fault"));
            Assert.That(discharged, Is.EqualTo(new[] { "remaining", "fault", "first" }));
        });
    }

    private static CompositionFrame CreateFrame(
        ICollection<string> discharged,
        params RecordedOperationSpec[] operations)
    {
        var drawable = new FaultingDrawable(operations) { Discharged = discharged };
        var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        return new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16));
    }
}

// Top-level partial because EngineObjectResourceGenerator does not support nested types.
internal sealed partial class FaultingDrawable(RecordedOperationSpec[] operations) : Drawable
{
    public ICollection<string> Discharged { get; set; } = new List<string>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new FixedOpsNode(operations, Discharged));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class FixedOpsNode : RenderNode
{
    private readonly Func<IReadOnlyList<RecordedOperationSpec>> _operationFactory;
    private readonly ICollection<string> _discharged;
    private readonly Action? _onProcess;
    private readonly Guid _recordingIdentity = Guid.NewGuid();

    public FixedOpsNode(
        IReadOnlyList<RecordedOperationSpec> operations,
        ICollection<string>? discharged = null)
        : this(() => operations, discharged)
    {
    }

    public FixedOpsNode(
        Func<IReadOnlyList<RecordedOperationSpec>> operationFactory,
        ICollection<string>? discharged = null,
        Action? onProcess = null)
    {
        _operationFactory = operationFactory;
        _discharged = discharged ?? new List<string>();
        _onProcess = onProcess;
    }

    public int ProcessCalls { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        ProcessCalls++;
        _onProcess?.Invoke();
        IReadOnlyList<RecordedOperationSpec> operations = _operationFactory();
        RenderResource<SolidColorBrush.Resource> fillResource = context.Borrow(
            Brushes.Resource.White,
            (typeof(FixedOpsNode), "white-fill"));
        for (int index = 0; index < operations.Count; index++)
        {
            RecordedOperationSpec spec = operations[index];
            bool trackDischarge = context.Purpose != RenderRequestPurpose.Bounds || spec.TrackMetadataDischarge;
            var operation = new RecordedOperation(spec, _discharged, trackDischarge);
            var resourceKey = (_recordingIdentity, index, context.Purpose);
            RenderResource<RecordedOperation> resource = context.Own(operation, resourceKey);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                execute: session => session.UseResource(
                    fillResource,
                    fill => RecordedOperation.Execute(spec, session, fill)),
                bounds: RenderOperationBoundsContract.Source(spec.EffectiveBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Vector,
                structuralKey: (typeof(FixedOpsNode), index),
                runtimeIdentity: new RenderRuntimeIdentity(resourceKey),
                resources: [resource, fillResource]);
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
    bool TrackMetadataDischarge = false,
    Rect? Bounds = null)
{
    public Rect EffectiveBounds => Bounds ?? new Rect(0, 0, 4, 4);
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
