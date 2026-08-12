using System.Reactive;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderNodeAuthoringContractTests
{
    private static readonly RenderResourceSlot<TrackingDisposable> s_ownedSlot = new();
    private static readonly RenderResourceSlot<TrackingDisposable> s_borrowedSlot = new();

    [Test]
    public void PublishingNothing_DropsInputs_WhilePassThroughPreservesOrderAndMetadata()
    {
        var firstBounds = new Rect(2, 3, 10, 20);
        var secondBounds = new Rect(30, 5, 4, 8);

        using var drop = new DelegateContainerNode(context =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(context.Inputs, Has.Count.EqualTo(2));
                Assert.That(
                    context.Inputs[0].TryGetMetadata(out RenderFragmentMetadata firstMetadata),
                    Is.True);
                Assert.That(firstMetadata.Bounds, Is.EqualTo(firstBounds));
                Assert.That(
                    context.Inputs[1].TryGetMetadata(out RenderFragmentMetadata secondMetadata),
                    Is.True);
                Assert.That(secondMetadata.Bounds, Is.EqualTo(secondBounds));
                Assert.That(context.TryCalculateInputBounds(out Rect inputBounds), Is.True);
                Assert.That(inputBounds, Is.EqualTo(firstBounds.Union(secondBounds)));
            });

            // Returning without publishing is the intentional no-output shape.
        });
        drop.AddChild(SourceNode(firstBounds));
        drop.AddChild(SourceNode(secondBounds));

        RenderNodeMeasurement dropped = Measure(drop);
        Assert.Multiple(() =>
        {
            Assert.That(dropped.HasFragments, Is.False);
            Assert.That(dropped.ValueCardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(dropped.OutputBounds, Is.EqualTo(default(Rect)));
        });

        using var passThrough = new DelegateContainerNode(context => context.PassThrough());
        passThrough.AddChild(SourceNode(firstBounds));
        passThrough.AddChild(SourceNode(secondBounds));

        RenderNodeMeasurement passed = Measure(passThrough);
        Assert.Multiple(() =>
        {
            Assert.That(passed.HasFragments, Is.True);
            Assert.That(passed.HasContributingValues, Is.True);
            Assert.That(passed.ValueCardinality, Is.EqualTo(RenderValueCardinality.Exactly(2)));
            Assert.That(passed.OutputBounds, Is.EqualTo(firstBounds.Union(secondBounds)));
        });
    }

    [Test]
    public void PublishMappedInputs_IsAvailableToExternalNodeAuthorsAndPreservesOrderedMetadata()
    {
        var firstBounds = new Rect(2, 3, 10, 20);
        var secondBounds = new Rect(30, 5, 4, 8);
        var mappedInputs = new List<FragmentSnapshot>();
        var mappedOutputs = new List<FragmentSnapshot>();

        using var simpleNode = new DelegateContainerNode(context =>
        {
            context.PublishMappedInputs(input =>
            {
                mappedInputs.Add(FragmentSnapshot.From(input));
                RenderFragmentHandle mapped = context.Opacity(input, 0.5f);
                mappedOutputs.Add(FragmentSnapshot.From(mapped));
                return mapped;
            });
        });
        simpleNode.AddChild(SourceNode(firstBounds));
        simpleNode.AddChild(SourceNode(secondBounds));

        RenderNodeMeasurement simpleMeasurement = Measure(simpleNode);

        var stateMappedBounds = new List<Rect>();
        using var stateNode = new DelegateContainerNode(context =>
            context.PublishMappedInputs(
                stateMappedBounds,
                static (current, input, observedBounds) =>
                {
                    Assert.That(input.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
                    observedBounds.Add(metadata.Bounds);
                    return current.Opacity(input, 0.75f);
                }));
        stateNode.AddChild(SourceNode(firstBounds));

        RenderNodeMeasurement stateMeasurement = Measure(stateNode);

        Assert.Multiple(() =>
        {
            Assert.That(
                mappedInputs.Select(static snapshot => snapshot.Bounds),
                Is.EqualTo(new[] { firstBounds, secondBounds }));
            Assert.That(mappedOutputs, Is.EqualTo(mappedInputs));
            Assert.That(simpleMeasurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Exactly(2)));
            Assert.That(simpleMeasurement.HasContributingValues, Is.True);
            Assert.That(simpleMeasurement.OutputBounds, Is.EqualTo(firstBounds.Union(secondBounds)));

            Assert.That(stateMappedBounds, Is.EqualTo(new[] { firstBounds }));
            Assert.That(stateMeasurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(stateMeasurement.HasContributingValues, Is.True);
            Assert.That(stateMeasurement.OutputBounds, Is.EqualTo(firstBounds));
        });
    }

    [Test]
    public void PublishMappedInputs_WithNoInputs_DoesNotInvokeMapperOrPublishOutputs()
    {
        var mapperCalls = new List<int>();
        using var node = new DelegateContainerNode(context =>
            context.PublishMappedInputs(
                mapperCalls,
                static (current, input, calls) =>
                {
                    calls.Add(0);
                    return current.Opacity(input, 0.5f);
                }));

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(mapperCalls, Is.Empty);
            Assert.That(measurement.HasFragments, Is.False);
            Assert.That(measurement.HasContributingValues, Is.False);
            Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(measurement.OutputBounds, Is.EqualTo(default(Rect)));
        });
    }

    [Test]
    public void PublishMappedInputs_RejectsMapperSidePublication()
    {
        using var node = new DelegateContainerNode(context =>
            context.PublishMappedInputs(input =>
            {
                context.Publish(input);
                return context.Opacity(input, 0.5f);
            }));
        node.AddChild(SourceNode(new Rect(0, 0, 10, 10)));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => Measure(node));

        Assert.That(exception!.Message, Does.Contain("must return its output without publishing fragments"));
    }

    [Test]
    public void OpaqueShapes_ExposeTheirApplicableCardinalityContributionAndValueEligibility()
    {
        var firstBounds = new Rect(0, 0, 10, 10);
        var secondBounds = new Rect(20, 5, 8, 12);
        var observed = new Dictionary<string, FragmentSnapshot>();

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle first = context.OpaqueSource(SourceDescription(
                firstBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector));
            RenderFragmentHandle second = context.OpaqueSource(SourceDescription(
                secondBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static _ => 2f)));
            RenderFragmentHandle map = context.OpaqueMap(
                first,
                MapDescription(RenderValueCardinality.ZeroOrOne));
            RenderFragmentHandle combine = context.OpaqueCombine(
                [first, second],
                CombineDescription(RenderValueCardinality.Single));
            RenderFragmentHandle expand = context.OpaqueExpand(
                [first, second],
                CombineDescription(RenderValueCardinality.Dynamic));
            RenderFragmentHandle emptyCombine = context.OpaqueCombine(
                [],
                EmptyInputDescription(RenderValueCardinality.ZeroOrOne));
            RenderFragmentHandle emptyExpand = context.OpaqueExpand(
                [],
                EmptyInputDescription(RenderValueCardinality.Dynamic));

            observed["source"] = FragmentSnapshot.From(first);
            observed["map"] = FragmentSnapshot.From(map);
            observed["combine"] = FragmentSnapshot.From(combine);
            observed["expand"] = FragmentSnapshot.From(expand);
            observed["empty-combine"] = FragmentSnapshot.From(emptyCombine);
            observed["empty-expand"] = FragmentSnapshot.From(emptyExpand);

            RenderFragmentHandle contributed = context.ContributeValues(emptyCombine);
            observed["contributed"] = FragmentSnapshot.From(contributed);
            Assert.That(context.ContributeValues(contributed), Is.SameAs(contributed));

            context.PublishRange([map, combine, expand, contributed]);
        });

        RenderNodeMeasurement measurement = Measure(node, outputScale: 1, maxWorkingScale: 4);

        Assert.Multiple(() =>
        {
            Assert.That(observed["source"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["source"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["source"].ContributesValues, Is.True);
            Assert.That(observed["source"].EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));

            Assert.That(observed["map"].Cardinality, Is.EqualTo(RenderValueCardinality.ZeroOrOne));
            Assert.That(observed["map"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["map"].ContributesValues, Is.True);

            Assert.That(observed["combine"].Bounds, Is.EqualTo(firstBounds.Union(secondBounds)));
            Assert.That(observed["combine"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["combine"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["combine"].ContributesValues, Is.True);

            Assert.That(observed["expand"].Cardinality, Is.EqualTo(RenderValueCardinality.Dynamic));
            Assert.That(observed["expand"].CanBeUsedAsValueInput, Is.True);

            Assert.That(observed["empty-combine"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["empty-combine"].ContributesValues, Is.False);
            Assert.That(observed["empty-expand"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["empty-expand"].ContributesValues, Is.False);
            Assert.That(observed["contributed"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["contributed"].ContributesValues, Is.True);

            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void TypedValueAndTargetWrappers_FollowThePublishedEligibilityTable()
    {
        var bounds = new Rect(4, 6, 20, 10);
        var observed = new Dictionary<string, FragmentSnapshot>();
        var fallbackBrush = new FallbackBrush();
        using Brush.Resource fallbackMask =
            (Brush.Resource)fallbackBrush.ToResource(CompositionContext.Default);

        using var node = new DelegateNode(context =>
        {
            RenderResource<Brush.Resource> whiteMask = context.Borrow((Brush.Resource)Brushes.Resource.White);
            RenderResource<Brush.Resource> fallbackMaskToken = context.Borrow(fallbackMask);
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(bounds));
            RenderFragmentHandle command = context.TargetCommand(
                [],
                RenderDefinitionCallFactory.TargetCommand(
                    static _ => throw new AssertionException("Metadata queries must not execute target commands."),
                    TargetRegion.Region(bounds),
                    bounds,
                    RenderHitTestContract.OutputBounds));

            RenderFragmentHandle opacityValue = context.Opacity(source, 0.5f);
            RenderFragmentHandle opacityCommand = context.Opacity(command, 0.5f);
            RenderFragmentHandle maskValue = context.OpacityMask(
                source,
                whiteMask,
                bounds);
            RenderFragmentHandle maskCommand = context.OpacityMask(
                command,
                whiteMask,
                bounds);
            RenderFragmentHandle maskFallback = context.OpacityMask(
                source,
                fallbackMaskToken,
                bounds);
            RenderFragmentHandle blend = context.Blend(source, BlendMode.SrcOver);
            RenderFragmentHandle targetScope = context.TargetScope(
                source,
                RenderDefinitionCallFactory.TargetScope(
                    static _ => throw new AssertionException("Metadata queries must not execute target scopes."),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply));
            RenderFragmentHandle targetLayer = context.TargetLayerScope(
                [source, command],
                TargetRegion.Region(bounds));
            RenderFragmentHandle layer = context.Layer([source, command], bounds);

            observed["opacity-value"] = FragmentSnapshot.From(opacityValue);
            observed["opacity-command"] = FragmentSnapshot.From(opacityCommand);
            observed["mask-value"] = FragmentSnapshot.From(maskValue);
            observed["mask-command"] = FragmentSnapshot.From(maskCommand);
            observed["mask-fallback"] = FragmentSnapshot.From(maskFallback);
            observed["blend"] = FragmentSnapshot.From(blend);
            observed["target-scope"] = FragmentSnapshot.From(targetScope);
            observed["command"] = FragmentSnapshot.From(command);
            observed["target-layer"] = FragmentSnapshot.From(targetLayer);
            observed["layer"] = FragmentSnapshot.From(layer);

            Assert.That(
                () => context.OpaqueMap(command, MapDescription(RenderValueCardinality.Single)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => context.ContributeValues(command), Throws.TypeOf<ArgumentException>());

            context.Drop(targetLayer);
            context.PublishRange(
                [opacityValue, maskValue, maskFallback, blend, targetScope, layer]);
        });

        _ = Measure(node, targetDomain: bounds);

        Assert.Multiple(() =>
        {
            Assert.That(observed["opacity-value"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["opacity-command"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["mask-value"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["mask-command"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["mask-fallback"].CanBeUsedAsValueInput, Is.True);
            Assert.That(observed["blend"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["target-scope"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["command"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["target-layer"].CanBeUsedAsValueInput, Is.False);
            Assert.That(observed["layer"].CanBeUsedAsValueInput, Is.True);

            Assert.That(observed["opacity-value"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["blend"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(observed["command"].Cardinality, Is.EqualTo(RenderValueCardinality.None));
            Assert.That(observed["target-layer"].Cardinality, Is.EqualTo(RenderValueCardinality.Exactly(1)));
            Assert.That(observed["layer"].Cardinality, Is.EqualTo(RenderValueCardinality.Single));
        });
    }

    [Test]
    public void MaterializedInputAndResourceTokens_AreUsableWithoutFriendAccessAndHonorOwnership()
    {
        var bounds = new Rect(10, 20, 10, 20);
        using RenderTarget target = RenderTarget.CreateNull(20, 40);
        var owned = new TrackingDisposable();
        var borrowed = new TrackingDisposable();
        FragmentSnapshot materialized = default;

        using var node = new DelegateNode(context =>
        {
            RenderResource<TrackingDisposable> ownedToken = context.Own(owned);
            RenderResource<TrackingDisposable> borrowedToken = context.Borrow(borrowed);
            RenderResource<RenderTarget> targetToken = context.Borrow(target);

            RenderFragmentHandle input = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    targetToken,
                    bounds,
                    EffectiveScale.At(2),
                    PixelRect.FromRect(bounds, 2),
                    default,
                    RenderHitTestContract.OutputBounds));
            materialized = FragmentSnapshot.From(input);

            RenderFragmentHandle declaredResourceSource = context.OpaqueSource(
                RenderDefinitionCallFactory.Opaque(
                    static _ => throw new AssertionException("Measure must not execute opaque callbacks."),
                    OpaqueRenderBoundsContract.Source(new Rect(0, 0, 1, 1)),
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    resources:
                    [
                        s_ownedSlot,
                        s_borrowedSlot,
                    ],
                    bindings:
                    [
                        s_ownedSlot.Bind(ownedToken),
                        s_borrowedSlot.Bind(borrowedToken),
                    ]));
            context.PublishRange([input, declaredResourceSource]);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(materialized.Bounds, Is.EqualTo(bounds));
            Assert.That(materialized.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(materialized.Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(materialized.CanBeUsedAsValueInput, Is.True);
            Assert.That(materialized.ContributesValues, Is.True);
            Assert.That(materialized.HitAtCenter, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(bounds.Union(new Rect(0, 0, 1, 1))));

            Assert.That(owned.DisposeCalls, Is.EqualTo(1));
            Assert.That(borrowed.DisposeCalls, Is.Zero);
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Publication_AllowsPureFanOut_ButRejectsEffectfulFanOut()
    {
        var bounds = new Rect(1, 2, 8, 6);
        using var pureNode = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(bounds));
            context.Publish(source);
            context.Publish(source);
        });

        RenderNodeMeasurement pureMeasurement = Measure(pureNode);
        Assert.Multiple(() =>
        {
            Assert.That(pureMeasurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Exactly(2)));
            Assert.That(pureMeasurement.OutputBounds, Is.EqualTo(bounds));
        });

        using var effectfulNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                RenderDefinitionCallFactory.TargetCommand(
                    static _ => { },
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None));
            context.Publish(command);
            context.Publish(command);
        });

        Assert.That(
            () => Measure(effectfulNode, targetDomain: bounds),
            Throws.TypeOf<InvalidOperationException>());

        using var indirectWrapperNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                RenderDefinitionCallFactory.TargetCommand(
                    static _ => { },
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None));
            context.PublishRange([
                context.Opacity(command, 0.5f),
                context.Opacity(command, 0.75f),
            ]);
        });
        using var duplicateLayerInputNode = new DelegateNode(context =>
        {
            RenderFragmentHandle command = context.TargetCommand(
                [],
                RenderDefinitionCallFactory.TargetCommand(
                    static _ => { },
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None));
            context.Publish(context.Layer([command, command], bounds));
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                () => Measure(indirectWrapperNode, targetDomain: bounds),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => Measure(duplicateLayerInputNode, targetDomain: bounds),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void CustomScaleAndRenderScaleUtilities_PreserveFeatureThreeDensityRules()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectiveScale observedScale = default;

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(
                bounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static scaleContext =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(scaleContext.InputSupplies, Is.Empty);
                        Assert.That(scaleContext.OutputBounds, Is.EqualTo(new Rect(0, 0, 100, 80)));
                        Assert.That(scaleContext.OutputScale, Is.EqualTo(1.5f));
                        Assert.That(scaleContext.MaxWorkingScale, Is.EqualTo(4));
                    });
                    return 6;
                })));
            Assert.That(source.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            observedScale = metadata.EffectiveScale;
            context.Publish(source);
        });

        _ = Measure(node, outputScale: 1.5f, maxWorkingScale: 4);

        EffectiveScale[] inputs = [EffectiveScale.Unbounded, EffectiveScale.At(2), EffectiveScale.At(3)];
        float clamped = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(0, 0, 20_000, 10),
            2);

        Assert.Multiple(() =>
        {
            Assert.That(observedScale, Is.EqualTo(EffectiveScale.At(4)));

            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(float.NaN), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(0), Is.EqualTo(float.PositiveInfinity));
            Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(3), Is.EqualTo(3));
            Assert.That(
                RenderScaleUtilities.ResolveWorkingScale(inputs, outputScale: 1.5f, maxWorkingScale: 2.5f),
                Is.EqualTo(2.5f));
            Assert.That(clamped, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(Math.Ceiling(20_000d * clamped), Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });

        using var invalidNode = new DelegateNode(context =>
        {
            _ = context.OpaqueSource(SourceDescription(
                bounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static _ => float.NaN)));
        });
        Assert.That(() => Measure(invalidNode), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void NestedRecording_ReturnsFreshValueEligibleFacadesWithPreservedMetadata()
    {
        var bounds = new Rect(7, 9, 11, 13);
        using var child = SourceNode(bounds);
        FragmentSnapshot nested = default;

        using var root = new DelegateNode(context =>
        {
            IReadOnlyList<RenderFragmentHandle> outputs = context.RecordNode(child, []);
            Assert.That(outputs, Has.Count.EqualTo(1));
            nested = FragmentSnapshot.From(outputs[0]);
            context.PublishRange(outputs);
        });

        RenderNodeMeasurement measurement = Measure(root);

        Assert.Multiple(() =>
        {
            Assert.That(nested.Bounds, Is.EqualTo(bounds));
            Assert.That(nested.Cardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(nested.CanBeUsedAsValueInput, Is.True);
            Assert.That(nested.ContributesValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void NestedRecording_SymbolicMetadataRemainsUnavailableUntilFiniteLayerResolvesIt()
    {
        var sourceBounds = new Rect(7, 9, 11, 13);
        var layerDomain = new Rect(2, 3, 40, 30);
        var effect = new UnknownBoundsPluginEffect();
        using FilterEffect.Resource effectResource = effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode filterNode = effectResource.CreateRenderNode();
        filterNode.AddChild(SourceNode(sourceBounds));
        using var backdropNode = new SnapshotBackdropRenderNode();

        using var root = new DelegateNode(context =>
        {
            RenderFragmentHandle symbolicFilter = context.RecordSubtree(filterNode).Single();
            RenderFragmentHandle symbolicBackdrop = context.RecordNode(backdropNode, []).Single();
            RenderFragmentHandle symbolicDescendant = context.Opacity(symbolicFilter, 0.5f);

            Assert.Multiple(() =>
            {
                Assert.That(
                    symbolicFilter.TryGetMetadata(out RenderFragmentMetadata filterMetadata),
                    Is.False);
                Assert.That(filterMetadata, Is.EqualTo(default(RenderFragmentMetadata)));
                Assert.That(symbolicFilter.TryHitTest(layerDomain.Center, out bool filterHit), Is.False);
                Assert.That(filterHit, Is.False);
                Assert.That(symbolicFilter.ValueCardinality, Is.EqualTo(RenderValueCardinality.Dynamic));
                Assert.That(symbolicFilter.ContributesValuesToTarget, Is.True);
                Assert.That(symbolicFilter.CanBeUsedAsValueInput, Is.True);

                Assert.That(
                    symbolicBackdrop.TryGetMetadata(out RenderFragmentMetadata backdropMetadata),
                    Is.False);
                Assert.That(backdropMetadata, Is.EqualTo(default(RenderFragmentMetadata)));
                Assert.That(symbolicBackdrop.TryHitTest(layerDomain.Center, out bool backdropHit), Is.False);
                Assert.That(backdropHit, Is.False);
                Assert.That(symbolicBackdrop.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
                Assert.That(symbolicBackdrop.ContributesValuesToTarget, Is.False);
                Assert.That(symbolicBackdrop.CanBeUsedAsValueInput, Is.True);

                Assert.That(
                    symbolicDescendant.TryGetMetadata(out RenderFragmentMetadata descendantMetadata),
                    Is.False);
                Assert.That(descendantMetadata, Is.EqualTo(default(RenderFragmentMetadata)));
                Assert.That(symbolicDescendant.TryHitTest(layerDomain.Center, out bool descendantHit), Is.False);
                Assert.That(descendantHit, Is.False);
                Assert.That(symbolicDescendant.ValueCardinality, Is.EqualTo(RenderValueCardinality.Dynamic));
                Assert.That(symbolicDescendant.ContributesValuesToTarget, Is.True);
                Assert.That(symbolicDescendant.CanBeUsedAsValueInput, Is.True);
            });

            RenderFragmentHandle layer = context.Layer([symbolicDescendant], layerDomain);
            Assert.Multiple(() =>
            {
                Assert.That(layer.TryGetMetadata(out RenderFragmentMetadata layerMetadata), Is.True);
                Assert.That(layerMetadata.Bounds, Is.EqualTo(layerDomain));
                Assert.That(layerMetadata.EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
                Assert.That(layer.TryHitTest(layerDomain.Center, out bool layerHit), Is.True);
                Assert.That(layerHit, Is.True);
                Assert.That(layer.TryHitTest(new Point(-100, -100), out bool outsideHit), Is.True);
                Assert.That(outsideHit, Is.False);
                Assert.That(layer.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
                Assert.That(layer.ContributesValuesToTarget, Is.True);
                Assert.That(layer.CanBeUsedAsValueInput, Is.True);
            });

            context.Publish(layer);
        });

        RenderNodeMeasurement measurement = Measure(root, targetDomain: layerDomain);
        Assert.That(measurement.OutputBounds, Is.EqualTo(layerDomain));
    }

    [Test]
    public void CardinalityFactoriesAndOpaqueTopologyValidation_ArePubliclyEnforced()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderValueCardinality.None, Is.EqualTo(RenderValueCardinality.Exactly(0)));
            Assert.That(RenderValueCardinality.Single, Is.EqualTo(RenderValueCardinality.Exactly(1)));
            Assert.That(RenderValueCardinality.ZeroOrOne, Is.EqualTo(RenderValueCardinality.Range(0, 1)));
            Assert.That(RenderValueCardinality.Dynamic, Is.EqualTo(RenderValueCardinality.Range(0, null)));
            Assert.That(() => RenderValueCardinality.Exactly(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => RenderValueCardinality.Range(-1, null), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => RenderValueCardinality.Range(2, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => RenderDefinitionCallFactory.Opaque(
                    static _ => { },
                    OpaqueRenderBoundsContract.Source(new Rect(0, 0, 1, 1)),
                    RenderHitTestContract.None,
                    default,
                    RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>());
        });

        using var invalidMap = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(new Rect(0, 0, 1, 1)));
            _ = context.OpaqueMap(source, MapDescription(RenderValueCardinality.Dynamic));
        });
        Assert.That(() => Measure(invalidMap), Throws.TypeOf<ArgumentException>());

        using var invalidCombine = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(new Rect(0, 0, 1, 1)));
            _ = context.OpaqueCombine(
                [source],
                CombineDescription(RenderValueCardinality.Exactly(2)));
        });
        Assert.That(() => Measure(invalidCombine), Throws.TypeOf<ArgumentException>());
    }

    private static DelegateNode SourceNode(Rect bounds)
    {
        return new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(bounds));
            context.Publish(source);
        });
    }

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> SourceDescription(Rect bounds)
        => SourceDescription(bounds, RenderValueCardinality.Single, RenderScaleContract.Vector);

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> SourceDescription(
        Rect bounds,
        RenderValueCardinality cardinality,
        RenderScaleContract scale)
    {
        return RenderDefinitionCallFactory.Opaque(
            static _ => throw new AssertionException("Metadata queries must not execute opaque callbacks."),
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            cardinality,
            scale);
    }

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> MapDescription(RenderValueCardinality cardinality)
    {
        return RenderDefinitionCallFactory.Opaque(
            static _ => throw new AssertionException("Metadata queries must not execute opaque callbacks."),
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            cardinality,
            RenderScaleContract.PreserveInputSupply);
    }

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> CombineDescription(RenderValueCardinality cardinality)
    {
        return RenderDefinitionCallFactory.Opaque(
            static _ => throw new AssertionException("Metadata queries must not execute opaque callbacks."),
            OpaqueRenderBoundsContract.FullInputs(UnionAll),
            RenderHitTestContract.AnyInput,
            cardinality,
            RenderScaleContract.MaterializeAtWorkingScale);
    }

    private static OpaqueRenderCall<Action<OpaqueRenderSession>> EmptyInputDescription(RenderValueCardinality cardinality)
    {
        return RenderDefinitionCallFactory.Opaque(
            static _ => throw new AssertionException("Metadata queries must not execute opaque callbacks."),
            OpaqueRenderBoundsContract.FullInputs(
                static _ => new Rect(40, 50, 3, 2)),
            RenderHitTestContract.OutputBounds,
            cardinality,
            RenderScaleContract.MaterializeAtWorkingScale);
    }

    private static Rect UnionAll(IReadOnlyList<Rect> inputs)
    {
        Rect result = default;
        foreach (Rect input in inputs)
        {
            result = result.Union(input);
        }

        return result;
    }

    private static RenderNodeMeasurement Measure(
        RenderNode node,
        Rect? targetDomain = null,
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
                    OutputScale = outputScale,
                    MaxWorkingScale = maxWorkingScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Measure();
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class DelegateContainerNode(Action<RenderNodeContext> process) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    [SuppressResourceClassGeneration]
    private sealed partial class UnknownBoundsPluginEffect : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
            => context.CustomEffect(Unit.Default, static (_, _) => { });

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }

    private readonly record struct FragmentSnapshot(
        Rect Bounds,
        EffectiveScale EffectiveScale,
        RenderValueCardinality Cardinality,
        bool ContributesValues,
        bool CanBeUsedAsValueInput,
        bool HitAtCenter)
    {
        public static FragmentSnapshot From(RenderFragmentHandle handle)
        {
            Assert.That(handle.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            Point center = new(
                metadata.Bounds.X + metadata.Bounds.Width / 2,
                metadata.Bounds.Y + metadata.Bounds.Height / 2);
            Assert.That(handle.TryHitTest(center, out bool hitAtCenter), Is.True);
            return new FragmentSnapshot(
                metadata.Bounds,
                metadata.EffectiveScale,
                handle.ValueCardinality,
                handle.ContributesValuesToTarget,
                handle.CanBeUsedAsValueInput,
                hitAtCenter);
        }
    }
}
