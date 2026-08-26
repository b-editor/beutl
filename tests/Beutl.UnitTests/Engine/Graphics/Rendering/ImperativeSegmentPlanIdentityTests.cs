using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that whether a effect-item filter segment holds an imperative callback is part of its plan identity.
/// </summary>
/// <remarks>
/// The segment's key carries its working-scale policy and stream-input count. Two segments that agree on
/// both must still not share a compiled plan when one runs an imperative callback: such a callback crops and
/// re-lays-out its targets in whole device pixels, so the executor strips the ambient sub-pixel phase for
/// that segment and ends its island for that reason. A plan compiled for one classification replayed against
/// the other describes a graph that is not there.
/// </remarks>
[TestFixture]
public sealed class ImperativeSegmentPlanIdentityTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    [Test]
    public void TwoSegmentsThatDifferOnlyInHoldingAnImperativeItem_AreDifferentPlans()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<FilterEffectContext> effectContext =
            registry.RegisterBorrowed(new FilterEffectContext(s_bounds));
        registry.Commit(effectContext);

        RenderFragmentReference input = Source();
        RenderFragmentReference declarative = Segment(input, effectContext, new DeclarativeItem());
        RenderFragmentReference imperative = Segment(input, effectContext, new ImperativeItem());
        var indexes = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance)
        {
            [input] = 0,
        };

        StructuralFragmentIdentity declarativeIdentity =
            StructuralFragmentIdentity.Create(declarative, indexes);
        StructuralFragmentIdentity imperativeIdentity =
            StructuralFragmentIdentity.Create(imperative, indexes);

        Assert.Multiple(() =>
        {
            Assert.That(
                declarativeIdentity,
                Is.EqualTo(StructuralFragmentIdentity.Create(declarative, indexes)),
                "the control: the same segment is the same plan");
            Assert.That(
                imperativeIdentity,
                Is.Not.EqualTo(declarativeIdentity),
                "an imperative callback classifies the island differently, so it cannot share the plan");
        });
    }

    private static RenderFragmentReference Source()
        => new(
            RenderFragmentKind.ContributeValues,
            s_bounds,
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [],
            payload: null,
            RenderFragmentHitTest.Bounds);

    private static RenderFragmentReference Segment(
        RenderFragmentReference input,
        RenderResource<FilterEffectContext> effectContext,
        IFEItem item)
        => new(
            RenderFragmentKind.FilterEffectSegment,
            s_bounds,
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [input],
            new FilterEffectSegmentRenderFragmentPayload(
                effectContext,
                ImmutableArray.Create(item),
                WorkingScalePolicy: null,
                StreamInputCount: 1),
            RenderFragmentHitTest.Bounds);

    private sealed class DeclarativeItem : IFEItem
    {
        public Rect TransformBounds(Rect bounds) => bounds;
    }

    private sealed class ImperativeItem : IFEItem, IFEItem_Custom
    {
        public Rect TransformBounds(Rect bounds) => bounds;

        public void Accepts(CustomFilterEffectContext context)
        {
        }
    }
}
