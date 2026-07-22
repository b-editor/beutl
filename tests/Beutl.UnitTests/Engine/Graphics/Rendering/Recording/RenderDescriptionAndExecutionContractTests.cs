using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderDescriptionAndExecutionContractTests
{
    [Test]
    public void OpaqueDescription_PreservesDeclaredContractsAndRejectsDefaults()
    {
        using var registry = new RenderRequestResourceRegistry();
        var value = new object();
        RenderResource<object> resource = registry.RegisterBorrowed(value, "resource", 3);
        Action<OpaqueRenderSession> execute = static _ => { };
        RenderOperationBoundsContract bounds = RenderOperationBoundsContract.Source(new Rect(2, 3, 10, 20));
        var runtimeIdentity = new RenderRuntimeIdentity(("pixels", 4));

        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            execute,
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "opaque-source",
            runtimeIdentity,
            requiresReadback: true,
            resources: [resource]);

        Assert.Multiple(() =>
        {
            Assert.That(description.Bounds, Is.SameAs(bounds));
            Assert.That(description.HitTest, Is.EqualTo(RenderHitTestContract.OutputBounds));
            Assert.That(description.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(description.Scale, Is.EqualTo(RenderScaleContract.MaterializeAtWorkingScale));
            Assert.That(description.StructuralKey, Is.EqualTo("opaque-source"));
            Assert.That(description.RuntimeIdentity, Is.EqualTo(runtimeIdentity));
            Assert.That(description.RequiresReadback, Is.True);
            Assert.That(description.Resources, Is.EqualTo(new[] { resource }));
            Assert.That(description.Execute, Is.SameAs(execute));
            Assert.That(() => description.ThrowIfIncompatible(OpaqueRenderTopology.Source, "description"),
                Throws.Nothing);
            Assert.That(() => description.ThrowIfIncompatible(OpaqueRenderTopology.Map, "description"),
                Throws.TypeOf<ArgumentException>());
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    null!, bounds, RenderHitTestContract.None, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    execute, null!, RenderHitTestContract.None, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    execute, bounds, default, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    execute, bounds, RenderHitTestContract.None, default, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    execute, bounds, RenderHitTestContract.None, RenderValueCardinality.Single, default),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    execute,
                    bounds,
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    runtimeIdentity: default(RenderRuntimeIdentity)),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void OperationBounds_ValidateTopologyAndMultiInputBackwardMapping()
    {
        Rect first = new(0, 0, 10, 20);
        Rect second = new(30, 5, 10, 10);
        Rect requested = new(4, 5, 6, 7);
        RenderOperationBoundsContract source = RenderOperationBoundsContract.Source(first);
        RenderOperationBoundsContract map = RenderOperationBoundsContract.Map(
            RenderBoundsContract.Create(
                static value => value.Translate(new Vector(3, 4)),
                static value => value.Translate(new Vector(-3, -4))));
        RenderOperationBoundsContract combine = RenderOperationBoundsContract.Combine(
            static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
            static (output, inputs) => inputs.Select(_ => output).ToArray(),
            "combine");
        RenderOperationBoundsContract full = RenderOperationBoundsContract.FullInputs(
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

        RenderOperationBoundsContract badCount = RenderOperationBoundsContract.Combine(
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
            static (context, point) => context.OutputBounds.Contains(point) && context.Inputs.Count == 2,
            "custom-hit");

        Assert.Multiple(() =>
        {
            Assert.That(RenderHitTestContract.None.Evaluate(output, inputs, new Point(12, 24)), Is.False);
            Assert.That(RenderHitTestContract.OutputBounds.Evaluate(output, inputs, new Point(12, 24)), Is.True);
            Assert.That(RenderHitTestContract.OutputBounds.Evaluate(output, inputs, new Point(1, 1)), Is.False);
            Assert.That(RenderHitTestContract.AnyInput.Evaluate(output, inputs, new Point(2, 3)), Is.True);
            Assert.That(custom.Evaluate(output, inputs, new Point(12, 24)), Is.True);
            Assert.That(inputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 5, 5)));
            Assert.That(inputs[0].HitTest(new Point(2, 3)), Is.True);
            Assert.That(() => default(RenderHitTestContract).Evaluate(output, inputs, default),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void ScaleContracts_ResolveConcreteSupplyAndRejectInvalidCustomResults()
    {
        EffectiveScale[] inputs = [EffectiveScale.At(1.5f), EffectiveScale.At(2.5f)];
        var bounds = new Rect(0, 0, 100, 100);
        RenderScaleContract custom = RenderScaleContract.Custom(
            static context => context.OutputScale * 3,
            "triple-output");

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
    public void MaterializedInput_RequiresConcreteMatchingBackingAndSourceHitTest()
    {
        using var registry = new RenderRequestResourceRegistry();
        using RenderTarget target = RenderTarget.CreateNull(20, 40);
        using RenderTarget wrongSize = RenderTarget.CreateNull(22, 40);
        RenderResource<RenderTarget> token = registry.RegisterBorrowed(target, "target", 1);
        var bounds = new Rect(10, 20, 10, 20);

        MaterializedInputDescription description = MaterializedInputDescription.FromRenderTarget(
            token,
            bounds,
            EffectiveScale.At(2),
            RenderHitTestContract.OutputBounds);

        Assert.Multiple(() =>
        {
            Assert.That(description.Bounds, Is.EqualTo(bounds));
            Assert.That(description.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(description.Target, Is.SameAs(token));
            Assert.That(description.HitTest, Is.EqualTo(RenderHitTestContract.OutputBounds));
            Assert.That(
                () => MaterializedInputDescription.FromRenderTarget(
                    token, bounds, EffectiveScale.Unbounded, RenderHitTestContract.None),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => description.ValidateTargetDeviceSize(wrongSize),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => MaterializedInputDescription.FromRenderTarget(
                    token, bounds, EffectiveScale.At(2), RenderHitTestContract.AnyInput),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void CaptureAndTargetDescriptions_ValidateRegionsReadbackAndIdentities()
    {
        var bounds = new Rect(1, 2, 30, 40);
        TargetCaptureDescription capture = TargetCaptureDescription.Create(
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderScaleContract.MaterializeAtWorkingScale);
        TargetCommandDescription command = TargetCommandDescription.Create(
            static _ => { },
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds,
            TargetAccess.Readback,
            requiresInputReadback: true,
            structuralKey: "read-command",
            runtimeIdentity: new RenderRuntimeIdentity(("command", 2)));
        TargetScopeDescription scope = TargetScopeDescription.Create(
            static _ => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            "scope");
        RawTargetScopeDescription rawScope = RawTargetScopeDescription.Create(
            static _ => { },
            RenderBoundsContract.FullInput,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            "raw-scope");
        RawTargetCommandDescription rawCommand = RawTargetCommandDescription.Create(
            static _ => { },
            bounds,
            RenderHitTestContract.OutputBounds,
            "raw-command");

        Assert.Multiple(() =>
        {
            Assert.That(capture.SourceRegion.Kind, Is.EqualTo(TargetRegionKind.Region));
            Assert.That(capture.Bounds, Is.EqualTo(bounds));
            Assert.That(command.Access, Is.EqualTo(TargetAccess.Readback));
            Assert.That(command.RequiresInputReadback, Is.True);
            Assert.That(command.QueryBounds, Is.EqualTo(bounds));
            Assert.That(scope.Bounds, Is.EqualTo(RenderBoundsContract.Identity));
            Assert.That(rawScope.Scale, Is.EqualTo(RenderScaleContract.PreserveInputSupply));
            Assert.That(rawCommand.QueryBounds, Is.EqualTo(bounds));
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Empty, bounds, RenderHitTestContract.None, RenderScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full, bounds, RenderHitTestContract.AnyInput, RenderScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full, bounds, RenderHitTestContract.None, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Region(new Rect(0, 0, 10, 10)),
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCommandDescription.Create(
                    static _ => { },
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCommandDescription.Create(
                    static _ => { },
                    default,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetScopeDescription.Create(
                    static _ => { }, default, RenderHitTestContract.None, RenderScaleContract.Vector),
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
            () => new ImmediateCanvas(target, 2, logicalSize: deviceBounds.Size.ToSize(2)),
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
            draw: (_, destination) => logicalPlacement = destination,
            drawDeviceSpace: (_, point) => devicePlacement = point,
            createShader: null,
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
            () => new ImmediateCanvas(callbackTarget, 2, logicalSize: callbackDeviceBounds.Size.ToSize(2)),
            CallbackCanvasCapability.Draw);
        using RenderTarget externalTarget = RenderTarget.CreateNull(8, 8);
        using var externalCanvas = new ImmediateCanvas(externalTarget);

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
            draw: static (_, _) => { },
            drawDeviceSpace: static (_, _) => { },
            createShader: null,
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
            () => new ImmediateCanvas(target, logicalSize: deviceBounds.Size.ToSize(1)),
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
