using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

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
