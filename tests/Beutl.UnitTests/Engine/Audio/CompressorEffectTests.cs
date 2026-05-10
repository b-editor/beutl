using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine.Audio;

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
        // Each IProperty<float> on the effect must be forwarded by reference to the corresponding
        // slot on CompressorNode. Reference equality (not just value equality) is critical because
        // the node reads CurrentValue and Animation through these references at process time —
        // copying the value would freeze it. A swap of any two properties (Threshold↔Ratio, etc.)
        // would silently corrupt the compressor without this test.
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
        // The contract is "audio flows input → compressor". Connect must run with arguments in
        // that order so the compressor's Inputs collection contains the upstream node, not the
        // other way around (which would route the compressor's own output into the input node
        // and produce silence at best, an infinite loop at worst).
        var effect = new CompressorEffect();
        using var context = new AudioContext(48000, 2);
        var inputNode = context.AddNode(new StubInputNode());

        var node = effect.CreateNode(context, inputNode);

        Assert.That(node.Inputs, Has.Count.EqualTo(1));
        Assert.That(node.Inputs[0], Is.SameAs(inputNode));
        // The returned node is the compressor itself, not the input — downstream effects need to
        // chain off the compressor.
        Assert.That(node, Is.Not.SameAs(inputNode));
    }

    [Test]
    public void CreateNode_DefaultPropertyValuesMatchCompressorParameters()
    {
        // The CompressorEffect's default values are sourced from CompressorParameters. If any
        // default drifted (e.g. the file was edited but not the [Range]), the effect would still
        // load but operators would see unexpected starting parameters. This guards against that
        // drift by asserting each property's CurrentValue is the documented default.
        var effect = new CompressorEffect();

        Assert.That(effect.Threshold.CurrentValue, Is.EqualTo(-20f));
        Assert.That(effect.Ratio.CurrentValue, Is.EqualTo(4f));
        Assert.That(effect.Attack.CurrentValue, Is.EqualTo(10f));
        Assert.That(effect.Release.CurrentValue, Is.EqualTo(100f));
        Assert.That(effect.Knee.CurrentValue, Is.EqualTo(6f));
        Assert.That(effect.MakeupGain.CurrentValue, Is.EqualTo(0f));
    }

    [Test]
    public void CompressorParameters_Validate_IsAnnotatedAsModuleInitializer()
    {
        // CompressorParameters.Validate is the only execution path for the Min/Default/Max
        // consistency asserts. Because every consumer references const fields (which the C#
        // compiler inlines), no `ldsfld` against CompressorParameters is ever emitted, so a
        // plain static constructor would not run. The class instead relies on
        // [ModuleInitializer] to invoke Validate at module load. If a future refactor removes
        // the attribute or renames the method without updating the contract, the asserts
        // become silent dead code. This reflection check fails fast if either invariant breaks.
        var method = typeof(CompressorParameters).GetMethod(
            "Validate",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null,
            "CompressorParameters.Validate must exist; it is the only carrier of the [ModuleInitializer] attribute.");
        Assert.That(method!.GetCustomAttribute<ModuleInitializerAttribute>(), Is.Not.Null,
            "CompressorParameters.Validate must be annotated [ModuleInitializer] so the Min/Default/Max asserts run at module load. Without it, the asserts become unreachable.");
    }
}
