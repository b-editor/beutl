using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderDescriptionAndExecutionContractTests
{

    [Test]
    public void OperationBounds_ValidateTopologyAndMultiInputBackwardMapping()
    {
        Rect first = new(0, 0, 10, 20);
        Rect second = new(30, 5, 10, 10);
        Rect requested = new(4, 5, 6, 7);
        OpaqueRenderBoundsContract source = OpaqueRenderBoundsContract.Source(first);
        OpaqueRenderBoundsContract map = OpaqueRenderBoundsContract.Map(
            RenderBoundsContract.Create(
                static value => value.Translate(new Vector(3, 4)),
                static value => value.Translate(new Vector(-3, -4))));
        OpaqueRenderBoundsContract combine = OpaqueRenderBoundsContract.Combine(
            static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
            static (output, inputs) => inputs.Select(_ => output).ToArray());
        OpaqueRenderBoundsContract full = OpaqueRenderBoundsContract.FullInputs(
            static inputs => inputs.Aggregate(static (left, right) => left.Union(right)));

        Assert.Multiple(() =>
        {
            Assert.That(source.TransformBounds([]), Is.EqualTo(first));
            Assert.That(map.TransformBounds([first]), Is.EqualTo(first.Translate(new Vector(3, 4))));
            Assert.That(map.GetRequiredInputBounds(requested, [first]), Is.EqualTo(new[]
            {
                requested.Translate(new Vector(-3, -4)),
            }));
            Assert.That(combine.TransformBounds([first, second]), Is.EqualTo(first.Union(second)));
            Assert.That(combine.GetRequiredInputBounds(requested, [first, second]),
                Is.EqualTo(new[] { requested, requested }));
            Assert.That(full.GetRequiredInputBounds(requested, [first, second]),
                Is.EqualTo(new[] { first, second }));
            Assert.That(
                () => combine.GetRequiredInputBounds(
                    requested,
                    [first]),
                Throws.Nothing);
        });

        OpaqueRenderBoundsContract badCount = OpaqueRenderBoundsContract.Combine(
            static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
            static (_, _) => [Rect.Empty]);
        Assert.That(
            () => badCount.GetRequiredInputBounds(requested, [first, second]),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(() => source.ThrowIfIncompatible(OpaqueRenderTopology.Source, "bounds"), Throws.Nothing);
            Assert.That(() => source.ThrowIfIncompatible(OpaqueRenderTopology.Map, "bounds"), Throws.TypeOf<ArgumentException>());
            Assert.That(() => map.ThrowIfIncompatible(OpaqueRenderTopology.Map, "bounds"), Throws.Nothing);
            Assert.That(() => combine.ThrowIfIncompatible(OpaqueRenderTopology.Combine, "bounds"), Throws.Nothing);
            Assert.That(() => full.ThrowIfIncompatible(OpaqueRenderTopology.Expand, "bounds"), Throws.Nothing);
        });
    }

    [Test]
    public void HitTestContracts_EvaluateOnlyDeclaredCpuMetadata()
    {
        var output = new Rect(10, 20, 30, 40);
        RenderHitTestInput[] inputs =
        [
            new(new Rect(0, 0, 5, 5), static point => point == new Point(2, 3)),
            new(new Rect(20, 20, 5, 5), static _ => false),
        ];
        RenderHitTestContract custom = RenderHitTestContract.Custom(
            static (context, point) => context.OutputBounds.Contains(point) && context.Inputs.Count == 2);

        Assert.Multiple(() =>
        {
            Assert.That(RenderHitTestContract.None.Evaluate(output, inputs, [], new Point(12, 24)), Is.False);
            Assert.That(RenderHitTestContract.OutputBounds.Evaluate(output, inputs, [], new Point(12, 24)), Is.True);
            Assert.That(RenderHitTestContract.OutputBounds.Evaluate(output, inputs, [], new Point(1, 1)), Is.False);
            Assert.That(RenderHitTestContract.AnyInput.Evaluate(output, inputs, [], new Point(2, 3)), Is.True);
            Assert.That(custom.Evaluate(output, inputs, [], new Point(12, 24)), Is.True);
            Assert.That(inputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 5, 5)));
            Assert.That(inputs[0].HitTest(new Point(2, 3)), Is.True);
            Assert.That(() => default(RenderHitTestContract).Evaluate(output, inputs, [], default),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void ScaleContracts_ResolveConcreteSupplyAndRejectInvalidCustomResults()
    {
        EffectiveScale[] inputs = [EffectiveScale.At(1.5f), EffectiveScale.At(2.5f)];
        var bounds = new Rect(0, 0, 100, 100);
        RenderScaleContract custom = RenderScaleContract.Custom(
            static context => context.OutputScale * 3);

        Assert.Multiple(() =>
        {
            Assert.That(RenderScaleContract.Vector.Resolve(inputs, bounds, 2, 4), Is.EqualTo(EffectiveScale.Unbounded));
            Assert.That(RenderScaleContract.MaterializeAtWorkingScale.Resolve(inputs, bounds, 2, 4),
                Is.EqualTo(EffectiveScale.At(2.5f)));
            Assert.That(custom.Resolve(inputs, bounds, 2, 4), Is.EqualTo(EffectiveScale.At(4)));
            Assert.That(
                RenderScaleContract.PreserveInputSupply.Resolve([EffectiveScale.At(3)], bounds, 2, 4),
                Is.EqualTo(EffectiveScale.At(3)));
            Assert.That(
                () => RenderScaleContract.PreserveInputSupply.Resolve(inputs, bounds, 2, 4),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => RenderScaleContract.Custom(static _ => float.NaN).Resolve(inputs, bounds, 2, 4),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => RenderScaleContract.Custom(static _ => float.PositiveInfinity).Resolve(inputs, bounds, 2, 4),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => default(RenderScaleContract).Resolve(inputs, bounds, 2, 4),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void ScaleContracts_ClampTheExactFractionalDeviceFootprint()
    {
        var positiveOrigin = new Rect(
            0.25f,
            0,
            RenderScaleUtilities.MaxBufferDimension,
            1);
        var exactFitAtNegativeOrigin = new Rect(
            -0.5f,
            0,
            RenderScaleUtilities.MaxBufferDimension - 0.5f,
            1);
        EffectiveScale[] resolved =
        [
            RenderScaleContract.MaterializeAtWorkingScale.Resolve(
                [EffectiveScale.At(1)],
                positiveOrigin,
                outputScale: 1,
                maxWorkingScale: 1),
            RenderScaleContract.Custom(
                    static _ => 1)
                .Resolve([], positiveOrigin, outputScale: 1, maxWorkingScale: 1),
            RenderScaleContract.MapInputSupplyPreservingDemand(
                    static _ => EffectiveScale.At(1))
                .Resolve([EffectiveScale.At(1)], positiveOrigin, outputScale: 1, maxWorkingScale: 1),
        ];

        Assert.Multiple(() =>
        {
            foreach (EffectiveScale scale in resolved)
            {
                Assert.That(scale.Value, Is.LessThan(1));
                Assert.That(
                    PixelRect.FromRect(positiveOrigin, scale.Value).Width,
                    Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            }

            Assert.That(
                PixelRect.FromRect(exactFitAtNegativeOrigin, 1).Width,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                RenderScaleContract.MaterializeAtWorkingScale.Resolve(
                    [EffectiveScale.At(1)],
                    exactFitAtNegativeOrigin,
                    outputScale: 1,
                    maxWorkingScale: 1),
                Is.EqualTo(EffectiveScale.At(1)));
        });
    }

    [Test]
    public void MaterializedInput_RequiresConcreteMatchingBackingAndSourceHitTest()
    {
        using var registry = new RenderRequestResourceRegistry();
        var bounds = new Rect(10.25f, 20.25f, 10, 20);
        var deviceGridOffset = new Vector(0.25f, 0.5f);
        PixelRect deviceBounds = PixelRect.FromRect(bounds.Translate(deviceGridOffset), 2);
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        using RenderTarget wrongSize = RenderTarget.CreateNull(deviceBounds.Width + 1, deviceBounds.Height);
        RenderResource<RenderTarget> token = registry.RegisterBorrowed(target);

        MaterializedInputDescription description = MaterializedInputDescription.FromRenderTarget(
            token,
            bounds,
            EffectiveScale.At(2),
            deviceBounds,
            deviceGridOffset,
            RenderHitTestContract.OutputBounds);

        Assert.Multiple(() =>
        {
            Assert.That(description.Bounds, Is.EqualTo(bounds));
            Assert.That(description.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(description.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(description.DeviceGridOffset, Is.EqualTo(deviceGridOffset));
            Assert.That(
                description.RasterBounds,
                Is.EqualTo(deviceBounds.ToRect(2).Translate(-deviceGridOffset)));
            Assert.That(description.Target, Is.SameAs(token));
            Assert.That(description.HitTest, Is.EqualTo(RenderHitTestContract.OutputBounds));
            Assert.That(
                () => MaterializedInputDescription.FromRenderTarget(
                    token,
                    bounds,
                    EffectiveScale.Unbounded,
                    deviceBounds,
                    deviceGridOffset,
                    RenderHitTestContract.None),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => description.ValidateTargetDeviceSize(wrongSize),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => MaterializedInputDescription.FromRenderTarget(
                    token,
                    bounds,
                    EffectiveScale.At(2),
                    deviceBounds,
                    deviceGridOffset,
                    RenderHitTestContract.AnyInput),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => MaterializedInputDescription.FromRenderTarget(
                    token,
                    bounds,
                    EffectiveScale.At(2),
                    new PixelRect(0, 0, deviceBounds.Width, deviceBounds.Height),
                    deviceGridOffset,
                    RenderHitTestContract.None),
                Throws.TypeOf<ArgumentException>());
        });
    }



    [Test]
    public void CallbackCanvas_MapsCompositionGlobalOriginAndEnforcesOneShotCapabilities()
    {
        var token = new RenderExecutionSessionToken();
        var logicalBounds = new Rect(10.25f, 20.25f, 8, 8);
        PixelRect deviceBounds = PixelRect.FromRect(logicalBounds, 2);
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var facade = new RenderCallbackCanvas(
            token,
            density: 2,
            logicalBounds,
            () => new ImmediateCanvas(target, RenderIntent.Preview, 2, logicalSize: deviceBounds.Size.ToSize(2)),
            CallbackCanvasCapability.Draw);
        ImmediateCanvas? retainedCanvas = null;

        facade.Use(canvas =>
        {
            retainedCanvas = canvas;
            Assert.Multiple(() =>
            {
                Assert.That(facade.DeviceBounds, Is.EqualTo(deviceBounds));
                Assert.That(facade.RasterBounds, Is.EqualTo(deviceBounds.ToRect(2)));
                Assert.That(facade.LogicalOrigin,
                    Is.EqualTo(new Point(deviceBounds.X / 2f, deviceBounds.Y / 2f)));
                Assert.That(canvas.Transform.Transform(facade.LogicalOrigin), Is.EqualTo(default(Point)));
                Assert.That(() => canvas.Clear(Colors.Red), Throws.Nothing);
                canvas.Pop(0);
                Assert.That(canvas.Transform.Transform(facade.LogicalOrigin), Is.EqualTo(default(Point)));
                Assert.That(() => canvas.PushLayer(), Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => canvas.DrawNode(null!), Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => RenderTarget.GetRenderTarget(canvas), Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => canvas.Dispose(), Throws.TypeOf<InvalidOperationException>());
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(retainedCanvas, Is.Not.Null);
            Assert.That(retainedCanvas!.IsDisposed, Is.True);
            Assert.That(() => retainedCanvas.Clear(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => facade.Use(static _ => { }), Throws.TypeOf<InvalidOperationException>());
        });

        token.Complete();
        Assert.That(() => _ = facade.Density, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ExecutionInput_RequiresActiveSameSessionCanvasAndUsesShiftedDevicePlacement()
    {
        var token = new RenderExecutionSessionToken();
        var inputBounds = new Rect(4, 6, 10, 12);
        Rect? logicalPlacement = null;
        Point? devicePlacement = null;
        var input = new RenderExecutionInput(
            token,
            inputBounds,
            EffectiveScale.At(2),
            draw: (_, destination, _, _) => logicalPlacement = destination,
            drawDeviceSpace: (_, point) => devicePlacement = point,
            createSnapshot: null,
            readbackDeclared: false);
        var callbackBounds = new Rect(10.25f, 20.25f, 8, 8);
        PixelRect callbackDeviceBounds = PixelRect.FromRect(callbackBounds, 2);
        using RenderTarget callbackTarget = RenderTarget.CreateNull(
            callbackDeviceBounds.Width,
            callbackDeviceBounds.Height);
        var facade = new RenderCallbackCanvas(
            token,
            2,
            callbackBounds,
            () => new ImmediateCanvas(callbackTarget, RenderIntent.Preview, 2, logicalSize: callbackDeviceBounds.Size.ToSize(2)),
            CallbackCanvasCapability.Draw);
        using RenderTarget externalTarget = RenderTarget.CreateNull(8, 8);
        using var externalCanvas = new ImmediateCanvas(externalTarget, RenderIntent.Preview);

        Assert.That(() => input.Draw(externalCanvas), Throws.TypeOf<InvalidOperationException>());

        facade.Use(canvas =>
        {
            input.Draw(canvas);
            input.DrawDeviceSpace(
                canvas,
                new Point(callbackDeviceBounds.X + 3, callbackDeviceBounds.Y + 5));
        });

        Assert.Multiple(() =>
        {
            Assert.That(logicalPlacement, Is.EqualTo(input.DeviceBounds.ToRect(2)));
            Assert.That(devicePlacement, Is.EqualTo(new Point(3, 5)));
            Assert.That(input.DeviceBounds, Is.EqualTo(PixelRect.FromRect(inputBounds, 2)));
            Assert.That(input.DeviceSize, Is.EqualTo(input.DeviceBounds.Size));
            Assert.That(input.RasterBounds, Is.EqualTo(input.DeviceBounds.ToRect(2)));
            Assert.That(input.LogicalOrigin,
                Is.EqualTo(new Point(input.DeviceBounds.X / 2f, input.DeviceBounds.Y / 2f)));
        });

        token.Complete();
        Assert.That(() => _ = input.Bounds, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ExecutionInput_ReadbackIsDeclaredOneShotAndDisposesOnCallbackFailure()
    {
        var token = new RenderExecutionSessionToken();
        Bitmap? supplied = null;
        var input = new RenderExecutionInput(
            token,
            new Rect(0, 0, 2, 2),
            EffectiveScale.At(1),
            draw: static (_, _, _, _) => { },
            drawDeviceSpace: static (_, _) => { },
            createSnapshot: () => supplied = new Bitmap(2, 2),
            readbackDeclared: true);
        var expected = new InvalidOperationException("callback failed");

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => input.UseSnapshot(bitmap =>
            {
                Assert.That(bitmap, Is.SameAs(supplied));
                throw expected;
            }));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(supplied, Is.Not.Null);
            Assert.That(supplied!.IsDisposed, Is.True);
            Assert.That(() => input.UseSnapshot(static _ => { }), Throws.TypeOf<InvalidOperationException>());
        });

        token.Complete();
    }

    [Test]
    public void TargetScopeCanvas_AllowsOnlyStateAroundExactlyOneReplay()
    {
        var token = new RenderExecutionSessionToken();
        var bounds = new Rect(5, 7, 10, 12);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var facade = new RenderCallbackCanvas(
            token,
            1,
            bounds,
            () => new ImmediateCanvas(target, RenderIntent.Preview, logicalSize: deviceBounds.Size.ToSize(1)),
            CallbackCanvasCapability.TargetScope);
        int replayCount = 0;
        var session = new TargetScopeSession(
            token,
            bounds,
            bounds,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            facade,
            [],
            canvas =>
            {
                replayCount++;
                using (canvas.PushLayer())
                {
                    canvas.Clear(Colors.Blue);
                }
            });

        Assert.That(() => session.ReplayInput(), Throws.TypeOf<InvalidOperationException>());
        facade.Use(canvas =>
        {
            Assert.That(() => canvas.Clear(), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => canvas.PushLayer(), Throws.TypeOf<InvalidOperationException>());
            using (canvas.PushTransform(Matrix.CreateTranslation(2, 3)))
            {
                session.ReplayInput();
            }

            Assert.That(() => session.ReplayInput(), Throws.TypeOf<InvalidOperationException>());
        });

        Assert.Multiple(() =>
        {
            Assert.That(replayCount, Is.EqualTo(1));
            Assert.That(() => session.ValidateCompletion(), Throws.Nothing);
        });

        token.Complete();
    }
}

internal static class RenderDescriptionAndExecutionContractSlots
{
    internal static readonly RenderResourceSlot<object> Resource = new();
}
