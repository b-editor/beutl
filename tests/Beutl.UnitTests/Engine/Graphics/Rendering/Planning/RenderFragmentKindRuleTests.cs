using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

// The planners throw on a kind they have no rule for, and a fragment derives its target-write flags from its kind
// when it is recorded, so a kind added without rules fails there first.
[TestFixture]
public sealed class RenderFragmentKindRuleTests
{
    [Test]
    public void EveryDefinedKind_HasTargetWriteRules()
    {
        Assert.Multiple(() =>
        {
            foreach (RenderFragmentKind kind in Enum.GetValues<RenderFragmentKind>())
                Assert.That(() => Create(kind), Throws.Nothing, kind.ToString());
        });
    }

    [Test]
    public void AnUndefinedKind_IsRefusedWhenRecorded()
    {
        Assert.That(() => Create((RenderFragmentKind)byte.MaxValue), Throws.InvalidOperationException);
    }

    private static RenderFragmentReference Create(RenderFragmentKind kind)
        => new(
            kind,
            new Rect(0, 0, 10, 10),
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: [],
            payload: null,
            hitTest: RenderFragmentHitTest.None);
}
