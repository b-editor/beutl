using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

using static Beutl.Audio.Effects.GateParameters;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class GateEffectTests
{
    private sealed class StubInputNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            return new AudioBuffer(context.SampleRate, 2, 0);
        }
    }

    [Test]
    public void CreateNode_WiresEveryPropertyToMatchingNodeSlot()
    {
        // Each property must be forwarded to its node slot by reference, not copied: the node reads
        // CurrentValue/Animation through these references at process time. Also catches a swap of any
        // two properties.
        var effect = new GateEffect();
        using var context = new AudioContext(48000, 2);
        var inputNode = context.AddNode(new StubInputNode());

        var node = effect.CreateNode(context, inputNode);

        Assert.That(node, Is.InstanceOf<GateNode>());
        var gate = (GateNode)node;

        Assert.That(gate.Threshold, Is.SameAs(effect.Threshold));
        Assert.That(gate.Attack, Is.SameAs(effect.Attack));
        Assert.That(gate.Hold, Is.SameAs(effect.Hold));
        Assert.That(gate.Release, Is.SameAs(effect.Release));
        Assert.That(gate.Range, Is.SameAs(effect.Range));
    }

    [Test]
    public void CreateNode_ConnectsInputAheadOfGate()
    {
        var effect = new GateEffect();
        using var context = new AudioContext(48000, 2);
        var inputNode = context.AddNode(new StubInputNode());

        var node = effect.CreateNode(context, inputNode);

        Assert.That(node.Inputs, Has.Count.EqualTo(1));
        Assert.That(node.Inputs[0], Is.SameAs(inputNode));
        Assert.That(node, Is.Not.SameAs(inputNode));
    }

    [Test]
    public void CreateNode_DefaultPropertyValuesMatchGateParameters()
    {
        var effect = new GateEffect();

        Assert.That(effect.Threshold.CurrentValue, Is.EqualTo(DefaultThresholdDb));
        Assert.That(effect.Attack.CurrentValue, Is.EqualTo(DefaultAttackMs));
        Assert.That(effect.Hold.CurrentValue, Is.EqualTo(DefaultHoldMs));
        Assert.That(effect.Release.CurrentValue, Is.EqualTo(DefaultReleaseMs));
        Assert.That(effect.Range.CurrentValue, Is.EqualTo(DefaultRangeDb));
    }

    [Test]
    public void ScanProperties_RegistersNamesAndRangeValidatorsForEveryProperty()
    {
        // ScanProperties (run in the constructor) must name each property after its CLR member and
        // attach the [Range] validator so out-of-range values are clamped at the engine layer.
        var effect = new GateEffect();

        AssertNameAndRange(effect.Threshold, nameof(effect.Threshold), MinThresholdDb, MaxThresholdDb);
        AssertNameAndRange(effect.Attack, nameof(effect.Attack), MinAttackMs, MaxAttackMs);
        AssertNameAndRange(effect.Hold, nameof(effect.Hold), MinHoldMs, MaxHoldMs);
        AssertNameAndRange(effect.Release, nameof(effect.Release), MinReleaseMs, MaxReleaseMs);
        AssertNameAndRange(effect.Range, nameof(effect.Range), MinRangeDb, MaxRangeDb);
    }

    private static void AssertNameAndRange(IProperty<float> property, string expectedName, float min, float max)
    {
        Assert.That(property.Name, Is.EqualTo(expectedName),
            $"ScanProperties must name the property '{expectedName}'.");

        property.CurrentValue = max + 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(max),
            $"{expectedName}: a value above the max must be coerced to {max}; validator appears unwired.");

        property.CurrentValue = min - 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(min),
            $"{expectedName}: a value below the min must be coerced to {min}; validator appears unwired.");
    }

    private static IEnumerable<TestCaseData> ParameterRanges()
    {
        yield return new TestCaseData(MinThresholdDb, DefaultThresholdDb, MaxThresholdDb).SetName("Threshold");
        yield return new TestCaseData(MinAttackMs, DefaultAttackMs, MaxAttackMs).SetName("Attack");
        yield return new TestCaseData(MinHoldMs, DefaultHoldMs, MaxHoldMs).SetName("Hold");
        yield return new TestCaseData(MinReleaseMs, DefaultReleaseMs, MaxReleaseMs).SetName("Release");
        yield return new TestCaseData(MinRangeDb, DefaultRangeDb, MaxRangeDb).SetName("Range");
    }

    [TestCaseSource(nameof(ParameterRanges))]
    public void GateParameters_RangeIsConsistent(float min, float def, float max)
    {
        Assert.That(min, Is.LessThan(max), "Min must be strictly less than Max.");
        Assert.That(def, Is.InRange(min, max), "Default must lie within [Min, Max].");
    }
}
