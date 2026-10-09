using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class AudioEffectLatencyContractTests : PublicApiContractTestBase
{
    private const int SampleRate = 48000;

    [TestCase(false, 340)]
    [TestCase(true, 580)]
    public void ExternalEffect_CanTransformAccumulatedLatencyThroughNestedGroups(bool mappingFirst, int expected)
    {
        AssertDoesNotHaveFriendAccess(typeof(AudioEffect).Assembly);
        var mapping = new PluginLatencyMappingEffect();
        var nested = new AudioEffectGroup();
        nested.Children.Add(mapping);
        var limiter = new LimiterEffect();
        limiter.Lookahead.CurrentValue = 10f;
        var group = new AudioEffectGroup();
        group.Children.Add(mappingFirst ? nested : limiter);
        group.Children.Add(mappingFirst ? limiter : nested);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode source = context.AddNode(new PluginLatencySourceNode(200));
        AudioNode output = group.CreateNode(context, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.GetLatencySamples(SampleRate), Is.Zero);
            Assert.That(group.GetLatencySamples(SampleRate, 200), Is.EqualTo(expected));
            Assert.That(output.GetTotalLatencySamples(SampleRate), Is.EqualTo(expected));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExternalEffect_DisabledOrSaturatedInputMatchesGraph(bool disabled)
    {
        var mapping = new PluginLatencyMappingEffect { IsEnabled = !disabled };
        var group = new AudioEffectGroup();
        group.Children.Add(mapping);
        using var context = new AudioContext(SampleRate, 2);
        int inputLatency = disabled ? 200 : int.MaxValue;
        AudioNode source = context.AddNode(new PluginLatencySourceNode(inputLatency));
        AudioNode output = group.CreateNode(context, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(group.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(output.GetTotalLatencySamples(SampleRate), Is.EqualTo(inputLatency));
        }
    }
}

internal sealed partial class PluginLatencyMappingEffect : AudioEffect
{
    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new PluginLatencyMappingNode());
        context.Connect(inputNode, node);
        return node;
    }

    public override int GetLatencySamples(int sampleRate, int inputLatency = 0)
    {
        base.GetLatencySamples(sampleRate, inputLatency);
        return !IsEnabled || inputLatency == int.MaxValue
            ? inputLatency
            : (int)Math.Ceiling(inputLatency / 2d);
    }
}

internal sealed class PluginLatencyMappingNode : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context) => Inputs[0].Process(context);

    public override int GetTotalLatencySamples(int sampleRate)
    {
        int inputLatency = base.GetTotalLatencySamples(sampleRate);
        return inputLatency == int.MaxValue ? inputLatency : (int)Math.Ceiling(inputLatency / 2d);
    }
}

internal sealed class PluginLatencySourceNode(int latency) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
        => new(context.SampleRate, 2, context.GetSampleCount());

    public override int GetLatencySamples(int sampleRate) => latency;
}
