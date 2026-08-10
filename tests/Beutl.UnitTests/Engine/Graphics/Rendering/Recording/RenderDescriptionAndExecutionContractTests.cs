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
        OpaqueRenderBoundsContract bounds = OpaqueRenderBoundsContract.Source(new Rect(2, 3, 10, 20));

        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            ("pixels", 4),
            static (_, _) => { },
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "opaque-source",
            resources: [resource.Bind("resource")]);
        OpaqueRenderDescription requestLocal = OpaqueRenderDescription.CreateRequestLocal(
            execute,
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "opaque-source-request-local");

        Assert.Multiple(() =>
        {
            Assert.That(description.Bounds, Is.SameAs(bounds));
            Assert.That(description.HitTest, Is.EqualTo(RenderHitTestContract.OutputBounds));
            Assert.That(description.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(description.Scale, Is.EqualTo(RenderScaleContract.MaterializeAtWorkingScale));
            Assert.That(description.StructuralKey, Is.EqualTo("opaque-source"));
            Assert.That(description.RuntimeIdentity, Is.Not.Null);
            Assert.That(requestLocal.RuntimeIdentity, Is.Null,
                "A request-local description publishes no identity a later request could match.");
            Assert.That(description.InputReadbacks, Is.Empty);
            Assert.That(description.Resources, Has.Count.EqualTo(1));
            Assert.That(description.Resources[0].Name, Is.EqualTo("resource"));
            Assert.That(description.Resources[0].Resource, Is.SameAs(resource));
            Assert.That(() => description.ThrowIfIncompatible(OpaqueRenderTopology.Source, "description"),
                Throws.Nothing);
            Assert.That(() => description.ThrowIfIncompatible(OpaqueRenderTopology.Map, "description"),
                Throws.TypeOf<ArgumentException>());
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    null!, bounds, RenderHitTestContract.None, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    execute, null!, RenderHitTestContract.None, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    execute, bounds, default, RenderValueCardinality.Single, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    execute, bounds, RenderHitTestContract.None, default, RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    execute, bounds, RenderHitTestContract.None, RenderValueCardinality.Single, default),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    execute,
                    bounds,
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    inputReadbacks: [default]),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("inputReadbacks"));
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    "state",
                    (session, _) => execute(session),
                    bounds,
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("execute"),
                "a capturing callback could draw with a value the state never carried");
        });
    }

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
            static (output, inputs) => inputs.Select(_ => output).ToArray(),
            "combine");
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
                    static _ => 1,
                    "exact-custom-scale")
                .Resolve([], positiveOrigin, outputScale: 1, maxWorkingScale: 1),
            RenderScaleContract.MapInputSupply(
                    static _ => EffectiveScale.At(1),
                    "exact-mapped-scale")
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
        RenderResource<RenderTarget> token = registry.RegisterBorrowed(target, "target", 1);

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
    public void CaptureAndTargetDescriptions_ValidateRegionsReadbackAndIdentities()
    {
        var bounds = new Rect(1, 2, 30, 40);
        TargetCaptureDescription capture = TargetCaptureDescription.Create(
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds,
            TargetCaptureScaleContract.MaterializeAtWorkingScale);
        TargetCommandDescription command = TargetCommandDescription.Create(
            ("command", 2),
            static (_, _) => { },
            TargetRegion.Region(bounds),
            bounds,
            RenderHitTestContract.OutputBounds,
            TargetAccess.Readback,
            inputReadbacks: [RenderInputReadback.Values([0])],
            structuralKey: "read-command");
        TargetScopeDescription scope = TargetScopeDescription.CreateRequestLocal(
            static _ => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
            structuralKey: "scope");
        RawTargetScopeDescription rawScope = RawTargetScopeDescription.CreateRequestLocal(
            static _ => { },
            RenderBoundsContract.FullInput,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            "raw-scope");
        RawTargetCommandDescription rawCommand = RawTargetCommandDescription.CreateRequestLocal(
            static _ => { },
            bounds,
            RenderHitTestContract.OutputBounds,
            "raw-command");

        Assert.Multiple(() =>
        {
            Assert.That(capture.SourceRegion.Kind, Is.EqualTo(TargetRegionKind.Region));
            Assert.That(capture.Bounds, Is.EqualTo(bounds));
            Assert.That(command.Access, Is.EqualTo(TargetAccess.Readback));
            Assert.That(command.InputReadbacks, Is.EqualTo(new[] { RenderInputReadback.Values([0]) }));
            Assert.That(RenderInputReadback.Values([1, 0]), Is.EqualTo(RenderInputReadback.Values([0, 1])));
            Assert.That(command.QueryBounds, Is.EqualTo(bounds));
            Assert.That(scope.Bounds, Is.EqualTo(RenderBoundsContract.Identity));
            Assert.That(rawScope.Scale, Is.EqualTo(RenderScaleContract.PreserveInputSupply));
            Assert.That(rawCommand.QueryBounds, Is.EqualTo(bounds));
            Assert.That(
                () => RenderInputReadback.Values([-1]),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => RenderInputReadback.Values([0, 0]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCommandDescription.CreateRequestLocal(
                    static _ => { },
                    TargetRegion.Region(bounds),
                    bounds,
                    RenderHitTestContract.None,
                    inputReadbacks: [default]),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("inputReadbacks"));
        });

        ArgumentException emptyReadback = Assert.Throws<ArgumentException>(
            () => TargetCommandDescription.CreateRequestLocal(
                static _ => { },
                TargetRegion.Empty,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.Readback))!;

        Assert.Multiple(() =>
        {
            Assert.That(emptyReadback.ParamName, Is.EqualTo("affectedRegion"));
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Empty, bounds, RenderHitTestContract.None, TargetCaptureScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full, bounds, RenderHitTestContract.AnyInput, TargetCaptureScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full, bounds, RenderHitTestContract.None, default),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Region(new Rect(0, 0, 10, 10)),
                    bounds,
                    RenderHitTestContract.None,
                    TargetCaptureScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCommandDescription.CreateRequestLocal(
                    static _ => { },
                    default,
                    Rect.Empty,
                    RenderHitTestContract.None),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetScopeDescription.CreateRequestLocal(
                    static _ => { },
                    default,
                    RenderHitTestContract.None,
                    RenderScaleContract.Vector,
                    deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void EveryDescriptionFactory_RejectsAnUndefinedDeclaredPlannerTrait()
    {
        const RenderDeviceGridSensitivity undefinedSensitivity = (RenderDeviceGridSensitivity)7;
        const RenderDeviceGridMapping undefinedMapping = (RenderDeviceGridMapping)7;
        OpaqueRenderBoundsContract opaqueBounds = OpaqueRenderBoundsContract.Source(new Rect(0, 0, 8, 8));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    static _ => { },
                    opaqueBounds,
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    undefinedSensitivity,
                    structuralKey: "undefined-sensitivity"),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("deviceGridSensitivity"));
            Assert.That(
                () => OpaqueRenderDescription.CreateEngineSource(
                    static _ => { },
                    static _ => { },
                    opaqueBounds,
                    RenderHitTestContract.OutputBounds,
                    RenderScaleContract.Vector,
                    undefinedSensitivity,
                    structuralKey: "undefined-sensitivity",
                    runtimeIdentity: null),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("deviceGridSensitivity"));
            Assert.That(
                () => OpaqueRenderDescription.CreateBackendBoundary(
                    RenderBackendBoundary.Graphics3D,
                    static _ => { },
                    opaqueBounds,
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    undefinedSensitivity,
                    structuralKey: "undefined-sensitivity",
                    runtimeIdentity: new RenderRuntimeIdentity("undefined-sensitivity")),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("deviceGridSensitivity"));
            Assert.That(
                () => TargetScopeDescription.CreateRequestLocal(
                    static session => session.Canvas.Use(_ => session.ReplayInput()),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                    deviceGridMapping: undefinedMapping,
                    structuralKey: "undefined-mapping"),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("deviceGridMapping"));
            Assert.That(
                () => TargetScopeDescription.CreateValueReplayMap(
                    static session => session.Canvas.Use(_ => session.ReplayInput()),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                    deviceGridMapping: undefinedMapping,
                    structuralKey: "undefined-mapping"),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("deviceGridMapping"));
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
