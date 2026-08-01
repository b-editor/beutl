using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class TargetAuthoringContractTests
{
    [Test]
    public void FiniteLayer_DistinguishesReadOnlyDependenciesFromPixelWrites()
    {
        var domain = new Rect(0, 0, 100, 80);
        var affected = new Rect(20, 30, 15, 10);
        FragmentSnapshot captureLayer = default;
        FragmentSnapshot commandLayer = default;

        using var captureNode = new DelegateNode(context =>
        {
            RenderFragmentHandle capture = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Region(affected),
                    affected,
                    RenderHitTestContract.None,
                    RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle layer = context.Layer([capture], domain);
            captureLayer = FragmentSnapshot.From(layer);
            context.Publish(layer);
        });
        using var commandNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => { },
                    TargetRegion.Region(affected),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "empty-query-finite-write"));
            RenderFragmentHandle layer = context.Layer([command], domain);
            commandLayer = FragmentSnapshot.From(layer);
            context.Publish(layer);
        });

        RenderNodeMeasurement captureMeasurement = Measure(captureNode, targetDomain: domain);
        RenderNodeMeasurement commandMeasurement = Measure(commandNode, targetDomain: domain);

        Assert.Multiple(() =>
        {
            Assert.That(captureLayer.ContributesValues, Is.False);
            Assert.That(captureLayer.Bounds, Is.EqualTo(default(Rect)));
            Assert.That(captureMeasurement.OutputBounds, Is.EqualTo(default(Rect)));
            Assert.That(commandLayer.ContributesValues, Is.True);
            Assert.That(commandLayer.Bounds, Is.EqualTo(affected));
            Assert.That(commandMeasurement.OutputBounds, Is.EqualTo(affected));
        });
    }

    [Test]
    public void TargetCommandsScopesAndLayers_ExposeDistinctMetadataAndEligibility()
    {
        var bounds = new Rect(5, 7, 20, 12);
        var queryBounds = new Rect(8, 9, 3, 4);
        var observed = new Dictionary<string, FragmentSnapshot>();

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(bounds));
            RenderFragmentHandle command = context.TargetCommand(
                [source],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute a guarded command."),
                    TargetRegion.Region(bounds),
                    queryBounds,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.ReadWrite,
                    structuralKey: "guarded-command"));
            RenderFragmentHandle rawCommand = context.RawTargetCommand(
                RawTargetCommandDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute a raw command."),
                    queryBounds,
                    RenderHitTestContract.OutputBounds,
                    structuralKey: "raw-command"));
            RenderFragmentHandle guardedScope = context.TargetScope(
                source,
                TargetScopeDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute a guarded scope."),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "guarded-scope"));
            RenderFragmentHandle rawScope = context.RawTargetScope(
                source,
                RawTargetScopeDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute a raw scope."),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "raw-scope"));
            RenderFragmentHandle targetLayer = context.TargetLayerScope(
                [source, command],
                TargetRegion.Region(bounds));
            RenderFragmentHandle finiteLayerCommand = context.TargetCommand(
                [source],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("Measure must not execute a guarded command."),
                    TargetRegion.Region(bounds),
                    queryBounds,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.ReadWrite,
                    structuralKey: "finite-layer-command"));
            RenderFragmentHandle finiteLayer = context.Layer([source, finiteLayerCommand], bounds);

            observed["command"] = FragmentSnapshot.From(command);
            observed["raw-command"] = FragmentSnapshot.From(rawCommand);
            observed["guarded-scope"] = FragmentSnapshot.From(guardedScope);
            observed["raw-scope"] = FragmentSnapshot.From(rawScope);
            observed["target-layer"] = FragmentSnapshot.From(targetLayer);
            observed["finite-layer"] = FragmentSnapshot.From(finiteLayer);

            Assert.That(() => context.Layer([source], Rect.Empty), Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => context.TargetLayerScope([source], default),
                Throws.TypeOf<ArgumentException>());

            context.PublishRange([guardedScope, rawScope, rawCommand, targetLayer, finiteLayer]);
        });

        RenderNodeMeasurement measurement = Measure(node, targetDomain: bounds);

        Assert.Multiple(() =>
        {
            Assert.That(observed["command"].Cardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(observed["command"].ContributesValues, Is.False);
            Assert.That(observed["command"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["command"].EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));

            Assert.That(observed["raw-command"].Cardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(observed["raw-command"].ContributesValues, Is.False);
            Assert.That(observed["raw-command"].CanBeUsedAsValueInput, Is.False);

            Assert.That(observed["guarded-scope"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["guarded-scope"].ContributesValues, Is.True);
            Assert.That(observed["guarded-scope"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["raw-scope"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["raw-scope"].CanBeUsedAsValueInput, Is.False);

            Assert.That(observed["target-layer"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["target-layer"].ContributesValues, Is.False);
            Assert.That(observed["target-layer"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["target-layer"].EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));

            Assert.That(observed["finite-layer"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["finite-layer"].ContributesValues, Is.True);
            Assert.That(observed["finite-layer"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["finite-layer"].Bounds, Is.EqualTo(bounds));

            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasTargetEffects, Is.True);
        });
    }

    [Test]
    public void FiniteTargetLayerScope_SeparatesAffectedRegionFromChildQueryMetadata()
    {
        var region = new Rect(0, 0, 100, 80);
        var childBounds = new Rect(20, 25, 12, 8);
        FragmentSnapshot scopeSnapshot = default;
        bool hitInsideChild = false;
        bool hitOutsideChild = true;

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(childBounds));
            RenderFragmentHandle scope = context.TargetLayerScope(
                [source],
                TargetRegion.Region(region));
            scopeSnapshot = FragmentSnapshot.From(scope);
            Assert.That(scope.TryHitTest(new Point(21, 26), out hitInsideChild), Is.True);
            Assert.That(scope.TryHitTest(new Point(1, 1), out hitOutsideChild), Is.True);
            context.Publish(scope);
        });

        RenderNodeMeasurement measurement = Measure(node, targetDomain: region);

        Assert.Multiple(() =>
        {
            Assert.That(scopeSnapshot.Bounds, Is.EqualTo(childBounds));
            Assert.That(hitInsideChild, Is.True);
            Assert.That(hitOutsideChild, Is.False);
            Assert.That(measurement.OutputBounds, Is.EqualTo(region));
            Assert.That(measurement.QueryBounds, Is.EqualTo(childBounds));
        });
    }

    [Test]
    public void SymbolicFullTargetAccess_RequiresARealTargetDomain_AndKeepsQueryBoundsSeparate()
    {
        var domain = new Rect(10, 20, 100, 60);
        var query = new Rect(30, 35, 8, 9);
        bool recorded = false;
        bool fullScopeMetadataAvailable = true;
        RenderFragmentMetadata fullScopeMetadata = default;
        bool fullScopeHitTestAvailable = true;
        bool fullScopeHit = true;
        bool descendantMetadataAvailable = true;
        RenderFragmentMetadata descendantMetadata = default;
        bool descendantHitTestAvailable = true;
        bool descendantHit = true;
        bool finiteLayerMetadataAvailable = false;
        RenderFragmentMetadata finiteLayerMetadata = default;
        bool finiteLayerHitTestAvailable = false;
        bool finiteLayerHit = false;

        using var commandNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => { },
                    TargetRegion.Full,
                    query,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.ReadWrite,
                    structuralKey: "full-writer"));
            recorded = true;
            context.Publish(command);
        });

        Assert.That(() => Measure(commandNode), Throws.TypeOf<RenderTargetDomainRequiredException>());
        Assert.That(recorded, Is.True, "Full remains symbolic during Process and fails only at graph finalization.");
        Assert.That(
            () => Measure(commandNode, requestedRegion: new Rect(20, 25, 5, 5)),
            Throws.TypeOf<RenderTargetDomainRequiredException>(),
            "RequestedRegion is not a substitute for TargetDomain.");

        RenderNodeMeasurement measurement = Measure(commandNode, targetDomain: domain);
        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(measurement.QueryBounds, Is.EqualTo(query));
            Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.False);
            Assert.That(measurement.HasTargetEffects, Is.True);
        });

        using var scopeNode = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(query));
            RenderFragmentHandle scope = context.TargetLayerScope([source], TargetRegion.Full);
            fullScopeMetadataAvailable = scope.TryGetMetadata(out fullScopeMetadata);
            fullScopeHitTestAvailable = scope.TryHitTest(new Point(31, 36), out fullScopeHit);

            RenderFragmentHandle descendant = context.Opacity(scope, 0.5f);
            descendantMetadataAvailable = descendant.TryGetMetadata(out descendantMetadata);
            descendantHitTestAvailable = descendant.TryHitTest(new Point(31, 36), out descendantHit);

            RenderFragmentHandle finiteLayer = context.Layer([scope], domain);
            finiteLayerMetadataAvailable = finiteLayer.TryGetMetadata(out finiteLayerMetadata);
            finiteLayerHitTestAvailable = finiteLayer.TryHitTest(new Point(11, 21), out finiteLayerHit);
            context.Publish(scope);
        });
        RenderNodeMeasurement scopeMeasurement = Measure(scopeNode, targetDomain: domain);
        Assert.Multiple(() =>
        {
            Assert.That(fullScopeMetadataAvailable, Is.False);
            Assert.That(fullScopeMetadata, Is.EqualTo(default(RenderFragmentMetadata)));
            Assert.That(fullScopeHitTestAvailable, Is.False);
            Assert.That(fullScopeHit, Is.False);
            Assert.That(descendantMetadataAvailable, Is.False);
            Assert.That(descendantMetadata, Is.EqualTo(default(RenderFragmentMetadata)));
            Assert.That(descendantHitTestAvailable, Is.False);
            Assert.That(descendantHit, Is.False);
            Assert.That(finiteLayerMetadataAvailable, Is.True);
            Assert.That(finiteLayerMetadata.Bounds, Is.EqualTo(domain));
            Assert.That(finiteLayerHitTestAvailable, Is.True);
            Assert.That(finiteLayerHit, Is.True);
            Assert.That(scopeMeasurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(scopeMeasurement.QueryBounds, Is.EqualTo(query));
            Assert.That(scopeMeasurement.HasTargetEffects, Is.True);
        });
    }

    [Test]
    public void SymbolicFullTargetScope_RemainsUnavailableToParentInputQueries()
    {
        var domain = new Rect(10, 20, 100, 60);
        var query = new Rect(30, 35, 8, 9);
        bool inputBoundsAvailable = true;
        Rect inputBounds = domain;
        using var parent = new DelegateContainerNode(context =>
        {
            inputBoundsAvailable = context.TryCalculateInputBounds(out inputBounds);
            context.PassThrough();
        });
        parent.AddChild(new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(query));
            context.Publish(context.TargetLayerScope([source], TargetRegion.Full));
        }));

        RenderNodeMeasurement measurement = Measure(parent, targetDomain: domain);

        Assert.Multiple(() =>
        {
            Assert.That(inputBoundsAvailable, Is.False);
            Assert.That(inputBounds, Is.EqualTo(default(Rect)));
            Assert.That(measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(measurement.QueryBounds, Is.EqualTo(query));
        });
    }

    [Test]
    public void TargetCapture_IsAReusableNonContributingValue_UntilContributionIsExplicit()
    {
        var bounds = new Rect(4, 6, 16, 10);
        var observed = new Dictionary<string, FragmentSnapshot>();

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle standard = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.OutputBounds,
                    RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle custom = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.Custom(static scaleContext =>
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(scaleContext.InputSupplies, Is.Empty);
                            Assert.That(scaleContext.OutputBounds, Is.EqualTo(new Rect(4, 6, 16, 10)));
                            Assert.That(scaleContext.OutputScale, Is.EqualTo(1.5f));
                            Assert.That(scaleContext.MaxWorkingScale, Is.EqualTo(2));
                        });
                        return 3;
                    }, "capture-custom-three")));
            RenderFragmentHandle contributed = context.ContributeValues(standard);

            observed["standard"] = FragmentSnapshot.From(standard);
            observed["custom"] = FragmentSnapshot.From(custom);
            observed["contributed"] = FragmentSnapshot.From(contributed);

            context.Publish(contributed);
            context.Publish(custom);
            context.Publish(custom); // Target capture is the effectful fan-out exception.
        });

        RenderNodeMeasurement measurement = Measure(
            node,
            targetDomain: bounds,
            outputScale: 1.5f,
            maxWorkingScale: 2);

        Assert.Multiple(() =>
        {
            Assert.That(observed["standard"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["standard"].ContributesValues, Is.False);
            Assert.That(observed["standard"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["standard"].EffectiveScale, Is.EqualTo(EffectiveScale.At(1.5f)));

            Assert.That(observed["custom"].ContributesValues, Is.False);
            Assert.That(observed["custom"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["custom"].EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));

            Assert.That(observed["contributed"].Bounds, Is.EqualTo(observed["standard"].Bounds));
            Assert.That(observed["contributed"].Cardinality, Is.EqualTo(observed["standard"].Cardinality));
            Assert.That(observed["contributed"].EffectiveScale, Is.EqualTo(observed["standard"].EffectiveScale));
            Assert.That(observed["contributed"].ContributesValues, Is.True);
            Assert.That(observed["contributed"].CanBeUsedAsValueInput, Is.True);

            Assert.That(measurement.OutputBounds, Is.EqualTo(bounds));
            Assert.That(measurement.QueryBounds, Is.EqualTo(bounds));
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.HasTargetEffects, Is.True);
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Empty,
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.MaterializeAtWorkingScale),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.PreserveInputSupply),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void TargetAndInputReadback_AreIndependentlyDeclaredAndConsumedOnce()
    {
        var bounds = new Rect(0, 0, 4, 3);
        int targetSnapshots = 0;
        int inputSnapshots = 0;
        TargetCommandDescription description = TargetCommandDescription.Create(
            session =>
            {
                session.UseSnapshot(_ => targetSnapshots++);
                Assert.That(session.Inputs, Has.Count.EqualTo(2));
                session.Inputs[0].UseSnapshot(_ => inputSnapshots++);
                Assert.That(
                    () => session.Inputs[1].UseSnapshot(_ => inputSnapshots++),
                    Throws.TypeOf<InvalidOperationException>());
            },
            TargetRegion.Region(bounds),
            Rect.Empty,
            RenderHitTestContract.None,
            TargetAccess.Readback,
            inputReadbacks: [RenderInputReadback.Values([0]), RenderInputReadback.None],
            structuralKey: "target-and-input-readback");

        Assert.Multiple(() =>
        {
            Assert.That(description.Access, Is.EqualTo(TargetAccess.Readback));
            Assert.That(description.InputReadbacks, Is.EqualTo(new[]
            {
                RenderInputReadback.Values([0]),
                RenderInputReadback.None,
            }));
            Assert.That(description.AffectedRegion, Is.EqualTo(TargetRegion.Region(bounds)));
            Assert.That(description.QueryBounds, Is.EqualTo(Rect.Empty));
        });

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle second = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle command = context.TargetCommand([source, second], description);
            context.PublishRange([source, second, command]);
        });

        using RenderNodeRasterization rasterization = Rasterize(node, targetDomain: bounds);
        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(targetSnapshots, Is.EqualTo(1));
            Assert.That(inputSnapshots, Is.EqualTo(1));
        });
    }

    [Test]
    public void FiniteTargetCommand_CanReplacePixelsWithTransparencyWithoutEscapingItsRegion()
    {
        var domain = new Rect(0, 0, 4, 3);
        var erased = new Rect(1, 1, 2, 1);
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(domain),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "finite-source-replace-background"));
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    session => session.ReplaceAffectedRegion(Colors.Transparent),
                    TargetRegion.Region(erased),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "finite-source-replace"));
            context.PublishRange([source, command]);
        });

        using RenderNodeRasterization rasterization = Rasterize(node, targetDomain: domain);
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The finite replacement request produced no bitmap.");
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        float outsideAlpha = (float)BitConverter.UInt16BitsToHalf(pixels[((0 * bitmap.Width) + 0) * 4 + 3]);
        float insideAlpha = (float)BitConverter.UInt16BitsToHalf(pixels[((1 * bitmap.Width) + 1) * 4 + 3]);

        Assert.Multiple(() =>
        {
            Assert.That(outsideAlpha, Is.EqualTo(1).Within(0.001f));
            Assert.That(insideAlpha, Is.Zero.Within(0.001f));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RenderInputReadback_BindsToAuthoredDynamicInputs(bool selectDynamicInput)
    {
        var bounds = new Rect(0, 0, 4, 3);
        int snapshots = 0;
        TargetCommandDescription description = TargetCommandDescription.Create(
            session =>
            {
                Assert.That(session.Inputs, Has.Count.EqualTo(3));
                for (int index = 0; index < session.Inputs.Count; index++)
                {
                    bool selected = selectDynamicInput ? index < 2 : index == 2;
                    if (selected)
                    {
                        session.Inputs[index].UseSnapshot(_ => snapshots++);
                    }
                    else
                    {
                        Assert.That(
                            () => session.Inputs[index].UseSnapshot(_ => snapshots++),
                            Throws.TypeOf<InvalidOperationException>());
                    }
                }
            },
            TargetRegion.Empty,
            Rect.Empty,
            RenderHitTestContract.None,
            TargetAccess.ReadWrite,
            inputReadbacks: selectDynamicInput
                ? [RenderInputReadback.All, RenderInputReadback.None]
                : [RenderInputReadback.None, RenderInputReadback.All],
            structuralKey: ("dynamic-input-readback", selectDynamicInput));

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle expanded = context.OpaqueExpand(
                [source],
                ExecutingExpansion(bounds));
            RenderFragmentHandle trailing = context.OpaqueSource(ExecutingSource(
                bounds,
                structuralKey: "trailing-readback-source"));
            RenderFragmentHandle command = context.TargetCommand([expanded, trailing], description);
            context.PublishRange([expanded, trailing, command]);
        });

        using RenderNodeRasterization rasterization = Rasterize(node, targetDomain: bounds);

        Assert.That(snapshots, Is.EqualTo(selectDynamicInput ? 2 : 1));
    }

    [Test]
    public void RenderInputReadback_RejectsMissingLocalRuntimeValue()
    {
        var bounds = new Rect(0, 0, 4, 3);
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle command = context.TargetCommand(
                [source],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("Invalid readback must fail before callback entry."),
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    inputReadbacks: [RenderInputReadback.Values([1])],
                    structuralKey: "missing-local-readback-value"));
            context.PublishRange([source, command]);
        });

        Assert.That(
            () => Rasterize(node, targetDomain: bounds),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("local input value index"));
    }

    [Test]
    public void RenderInputReadback_RequiresOneDeclarationPerAuthoredInput()
    {
        var bounds = new Rect(0, 0, 4, 3);
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            _ = context.TargetCommand(
                [source],
                TargetCommandDescription.Create(
                    static _ => { },
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    inputReadbacks: [RenderInputReadback.All, RenderInputReadback.None],
                    structuralKey: "mismatched-input-readback-count"));
        });

        Assert.That(
            () => Rasterize(node, targetDomain: bounds),
            Throws.TypeOf<ArgumentException>()
                .With.Property("ParamName").EqualTo("description"));
    }

    [Test]
    public void OpaqueInputReadback_SelectsRuntimeValuesPerAuthoredInput()
    {
        var bounds = new Rect(0, 0, 4, 3);
        int snapshots = 0;
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                Assert.That(session.Inputs, Has.Count.EqualTo(3));
                for (int index = 0; index < session.Inputs.Count; index++)
                {
                    if (index == 1)
                    {
                        session.Inputs[index].UseSnapshot(_ => snapshots++);
                    }
                    else
                    {
                        Assert.That(
                            () => session.Inputs[index].UseSnapshot(_ => snapshots++),
                            Throws.TypeOf<InvalidOperationException>());
                    }
                }

                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.FullInputs(
                static inputs => inputs.Aggregate(static (left, right) => left.Union(right))),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "opaque-selective-input-readback",
            inputReadbacks: [RenderInputReadback.Values([1]), RenderInputReadback.None]);

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle expanded = context.OpaqueExpand([source], ExecutingExpansion(bounds));
            RenderFragmentHandle trailing = context.OpaqueSource(ExecutingSource(
                bounds,
                structuralKey: "opaque-readback-trailing-source"));
            context.Publish(context.OpaqueCombine([expanded, trailing], description));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, targetDomain: bounds);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(snapshots, Is.EqualTo(1));
            Assert.That(description.InputReadbacks, Is.EqualTo(new[]
            {
                RenderInputReadback.Values([1]),
                RenderInputReadback.None,
            }));
        });
    }

    [Test]
    public void OpaqueExpand_CreatesOutputsAtIndependentDensities()
    {
        var bounds = new Rect(0, 0, 12, 8);
        var observedScales = new List<float>();
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput large = session.CreateOutput(new Rect(0, 0, 8, 8), density: 1);
                    using OpaqueRenderOutput detail = session.CreateOutput(new Rect(8, 0, 4, 4), density: 3);
                    observedScales.Add(large.EffectiveScale.Value);
                    observedScales.Add(detail.EffectiveScale.Value);
                    session.Publish(large);
                    session.Publish(detail);
                },
                OpaqueRenderBoundsContract.FullInputs(static inputs => inputs.Single()),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Dynamic,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "independent-opaque-output-densities");
            context.Publish(context.OpaqueExpand([source], description));
        });

        using RenderNodeRasterization rasterization = Rasterize(
            node,
            targetDomain: bounds,
            maxWorkingScale: 4);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(observedScales, Is.EqualTo(new[] { 1f, 3f }));
        });
    }

    [Test]
    public void GuardedAndRawTargetCallbacks_ExecuteInAuthoredPainterOrder()
    {
        var bounds = new Rect(0, 0, 6, 5);
        var order = new List<string>();

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle first = context.OpaqueSource(ExecutingSource(bounds, () => order.Add("A"), "A"));
            RenderFragmentHandle guarded = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    _ => order.Add("guarded"),
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "ordered-guarded"));
            RenderFragmentHandle raw = context.RawTargetCommand(
                RawTargetCommandDescription.Create(
                    _ => order.Add("raw"),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    structuralKey: "ordered-raw"));
            RenderFragmentHandle second = context.OpaqueSource(ExecutingSource(bounds, () => order.Add("B"), "B"));

            context.PublishRange([first, guarded, raw, second]);
        });

        using RenderNodeRasterization rasterization = Rasterize(node, targetDomain: bounds);
        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(order, Is.EqualTo(new[] { "A", "guarded", "raw", "B" }));
        });
    }

    [Test]
    public void TargetScopes_ReplayTheirInputExactlyOnceThroughPublicSessions()
    {
        var bounds = new Rect(0, 0, 5, 4);
        int guardedReplays = 0;
        int rawReplays = 0;

        using var guardedNode = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle scope = context.TargetScope(
                source,
                TargetScopeDescription.Create(
                    session => session.Canvas.Use(_ =>
                    {
                        session.ReplayInput();
                        guardedReplays++;
                    }),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "executing-guarded-scope"));
            context.Publish(scope);
        });
        using (RenderNodeRasterization rasterization = Rasterize(guardedNode, targetDomain: bounds))
        {
            Assert.That(rasterization.IsEmpty, Is.False);
        }

        using var rawNode = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds));
            RenderFragmentHandle scope = context.RawTargetScope(
                source,
                RawTargetScopeDescription.Create(
                    session =>
                    {
                        session.ReplayInput();
                        rawReplays++;
                    },
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "executing-raw-scope"));
            context.Publish(scope);
        });
        using (RenderNodeRasterization rasterization = Rasterize(rawNode, targetDomain: bounds))
        {
            Assert.That(rasterization.IsEmpty, Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(guardedReplays, Is.EqualTo(1));
            Assert.That(rawReplays, Is.EqualTo(1));
        });
    }

    [Test]
    public void CaptureInsideDenserFiniteLayer_UsesItsDeclaredOutputDerivedDensity()
    {
        var bounds = new Rect(0, 0, 4, 4);
        float captureInputScale = 0;
        float layerTargetScale = 0;
        float outerInputScale = 0;

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(
                bounds,
                scale: RenderScaleContract.Vector,
                structuralKey: "dense-layer-source"));
            RenderFragmentHandle capture = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    bounds,
                    RenderHitTestContract.None,
                    RenderScaleContract.Custom(static scaleContext =>
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(scaleContext.InputSupplies, Is.Empty);
                            Assert.That(scaleContext.OutputBounds, Is.EqualTo(new Rect(0, 0, 4, 4)));
                            Assert.That(scaleContext.OutputScale, Is.EqualTo(1));
                            Assert.That(scaleContext.MaxWorkingScale, Is.EqualTo(4));
                        });
                        return scaleContext.OutputScale;
                    }, "output-derived-capture")));
            RenderFragmentHandle inspectCapture = context.TargetCommand(
                [capture],
                TargetCommandDescription.Create(
                    session =>
                    {
                        captureInputScale = session.Inputs.Single().EffectiveScale.Value;
                        layerTargetScale = session.Canvas.Density;
                    },
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "inspect-capture-density"));
            RenderFragmentHandle layer = context.Layer([source, capture, inspectCapture], bounds);
            RenderFragmentHandle forceDenseMaterialization = context.OpaqueMap(
                layer,
                ExecutingMap(
                    bounds,
                    session => outerInputScale = session.Inputs.Single().EffectiveScale.Value,
                    RenderScaleContract.Custom(static _ => 4, "materialize-layer-at-four")));

            context.Publish(forceDenseMaterialization);
        });

        using RenderNodeRasterization rasterization = Rasterize(
            node,
            outputScale: 1,
            maxWorkingScale: 4,
            requestedRegion: bounds);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(captureInputScale, Is.EqualTo(1), "The capture remains an output-density value.");
            Assert.That(layerTargetScale, Is.EqualTo(4), "The finite Layer is materialized for the denser consumer.");
            Assert.That(outerInputScale, Is.EqualTo(4), "The dense consumer receives the Layer at its resolved supply.");
        });
    }

    private static OpaqueRenderDescription MetadataSource(Rect bounds)
    {
        return OpaqueRenderDescription.Create(
            static _ => throw new AssertionException("Measure must not execute opaque source callbacks."),
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            structuralKey: ("metadata-source", bounds));
    }

    private static OpaqueRenderDescription ExecutingSource(
        Rect bounds,
        Action? beforePublish = null,
        object? structuralKey = null,
        RenderScaleContract? scale = null)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                beforePublish?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            scale ?? RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: structuralKey ?? ("executing-source", bounds),
            runtimeIdentity: new RenderRuntimeIdentity(("source-runtime", structuralKey ?? bounds)));
    }

    private static OpaqueRenderDescription ExecutingMap(
        Rect bounds,
        Action<OpaqueRenderSession> observe,
        RenderScaleContract scale)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                observe(session);
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => session.Inputs.Single().Draw(canvas));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            scale,
            structuralKey: ("executing-map", bounds));
    }

    private static OpaqueRenderDescription ExecutingExpansion(Rect bounds)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                for (int index = 0; index < 2; index++)
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    session.Publish(output);
                }
            },
            OpaqueRenderBoundsContract.FullInputs(
                static inputs => inputs.Aggregate(static (left, right) => left.Union(right))),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Dynamic,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: ("executing-expansion", bounds),
            runtimeIdentity: new RenderRuntimeIdentity(("expansion-runtime", bounds)));
    }

    private static RenderNodeMeasurement Measure(
        RenderNode node,
        Rect? targetDomain = null,
        Rect? requestedRegion = null,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = targetDomain,
                    RequestedRegion = requestedRegion,
                    OutputScale = outputScale,
                    MaxWorkingScale = maxWorkingScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(
        RenderNode node,
        Rect? targetDomain = null,
        Rect? requestedRegion = null,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = targetDomain,
                    RequestedRegion = requestedRegion,
                    OutputScale = outputScale,
                    MaxWorkingScale = maxWorkingScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Rasterize();
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class DelegateContainerNode(Action<RenderNodeContext> process) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private readonly record struct FragmentSnapshot(
        Rect Bounds,
        EffectiveScale EffectiveScale,
        RenderValueCardinality Cardinality,
        bool ContributesValues,
        bool CanBeUsedAsValueInput)
    {
        public static FragmentSnapshot From(RenderFragmentHandle handle)
        {
            Assert.That(handle.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            return new FragmentSnapshot(
                metadata.Bounds,
                metadata.EffectiveScale,
                handle.ValueCardinality,
                handle.ContributesValuesToTarget,
                handle.CanBeUsedAsValueInput);
        }
    }
}
