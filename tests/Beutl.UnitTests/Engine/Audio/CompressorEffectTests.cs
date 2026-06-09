using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class CompressorEffectTests
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
        // CurrentValue/Animation through these references at process time, and copying would freeze
        // the value. Also catches a swap of any two properties (Threshold↔Ratio, etc.).
        var effect = new CompressorEffect();
        using var context = new AudioContext(48000, 2);
        var inputNode = context.AddNode(new StubInputNode());

        var node = effect.CreateNode(context, inputNode);

        Assert.That(node, Is.InstanceOf<CompressorNode>());
        var compressor = (CompressorNode)node;

        Assert.That(compressor.Threshold, Is.SameAs(effect.Threshold));
        Assert.That(compressor.Ratio, Is.SameAs(effect.Ratio));
        Assert.That(compressor.Attack, Is.SameAs(effect.Attack));
        Assert.That(compressor.Release, Is.SameAs(effect.Release));
        Assert.That(compressor.Knee, Is.SameAs(effect.Knee));
        Assert.That(compressor.MakeupGain, Is.SameAs(effect.MakeupGain));
    }

    [Test]
    public void CreateNode_ConnectsInputAheadOfCompressor()
    {
        // Audio flows input → compressor, so the compressor's Inputs must hold the upstream node.
        // The reverse would route the compressor's output into the input (silence or an infinite loop).
        var effect = new CompressorEffect();
        using var context = new AudioContext(48000, 2);
        var inputNode = context.AddNode(new StubInputNode());

        var node = effect.CreateNode(context, inputNode);

        Assert.That(node.Inputs, Has.Count.EqualTo(1));
        Assert.That(node.Inputs[0], Is.SameAs(inputNode));
        // The returned node is the compressor, not the input, so downstream effects chain off it.
        Assert.That(node, Is.Not.SameAs(inputNode));
    }

    [Test]
    public void CreateNode_DefaultPropertyValuesMatchCompressorParameters()
    {
        // Defaults come from CompressorParameters; assert each CurrentValue matches so a drifted
        // default (file edited but not the [Range]) is caught.
        var effect = new CompressorEffect();

        Assert.That(effect.Threshold.CurrentValue, Is.EqualTo(-20f));
        Assert.That(effect.Ratio.CurrentValue, Is.EqualTo(4f));
        Assert.That(effect.Attack.CurrentValue, Is.EqualTo(10f));
        Assert.That(effect.Release.CurrentValue, Is.EqualTo(100f));
        Assert.That(effect.Knee.CurrentValue, Is.EqualTo(6f));
        Assert.That(effect.MakeupGain.CurrentValue, Is.EqualTo(0f));
    }

    [Test]
    public void ScanProperties_RegistersNamesAndRangeValidatorsForEveryProperty()
    {
        // ScanProperties (run in the constructor) must name each property after its CLR member and
        // attach the [Range] validator so out-of-range values are clamped at the engine layer, not
        // just in the UI. Assert the name and exercise the clamp at both bounds.
        var effect = new CompressorEffect();

        AssertNameAndRange(effect.Threshold, nameof(effect.Threshold), MinThresholdDb, MaxThresholdDb);
        AssertNameAndRange(effect.Ratio, nameof(effect.Ratio), MinRatio, MaxRatio);
        AssertNameAndRange(effect.Attack, nameof(effect.Attack), MinAttackMs, MaxAttackMs);
        AssertNameAndRange(effect.Release, nameof(effect.Release), MinReleaseMs, MaxReleaseMs);
        AssertNameAndRange(effect.Knee, nameof(effect.Knee), MinKneeDb, MaxKneeDb);
        AssertNameAndRange(effect.MakeupGain, nameof(effect.MakeupGain), MinMakeupGainDb, MaxMakeupGainDb);
    }

    private static void AssertNameAndRange(IProperty<float> property, string expectedName, float min, float max)
    {
        Assert.That(property.Name, Is.EqualTo(expectedName),
            $"ScanProperties must name the property '{expectedName}'.");

        // Above-max assignment must clamp down to max (proves the [Range] validator is wired).
        property.CurrentValue = max + 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(max),
            $"{expectedName}: a value above the max must be coerced to {max}; validator appears unwired.");

        // Below-min assignment must clamp up to min.
        property.CurrentValue = min - 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(min),
            $"{expectedName}: a value below the min must be coerced to {min}; validator appears unwired.");
    }

    private static IEnumerable<TestCaseData> ParameterRanges()
    {
        yield return new TestCaseData(MinThresholdDb, DefaultThresholdDb, MaxThresholdDb).SetName("Threshold");
        yield return new TestCaseData(MinRatio, DefaultRatio, MaxRatio).SetName("Ratio");
        yield return new TestCaseData(MinAttackMs, DefaultAttackMs, MaxAttackMs).SetName("Attack");
        yield return new TestCaseData(MinReleaseMs, DefaultReleaseMs, MaxReleaseMs).SetName("Release");
        yield return new TestCaseData(MinKneeDb, DefaultKneeDb, MaxKneeDb).SetName("Knee");
        yield return new TestCaseData(MinMakeupGainDb, DefaultMakeupGainDb, MaxMakeupGainDb).SetName("MakeupGain");
    }

    [TestCaseSource(nameof(ParameterRanges))]
    public void CompressorParameters_RangeIsConsistent(float min, float def, float max)
    {
        // Catches an edit that puts a default outside its range or inverts a min/max, at CI time —
        // the role formerly served by a [ModuleInitializer] + Debug.Assert (invisible in Release).
        Assert.That(min, Is.LessThan(max), "Min must be strictly less than Max.");
        Assert.That(def, Is.InRange(min, max), "Default must lie within [Min, Max].");
    }
}
