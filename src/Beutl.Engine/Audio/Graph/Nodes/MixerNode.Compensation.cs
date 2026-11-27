using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

public sealed partial class MixerNode
{
    private Dictionary<AudioNode, BranchDelay>? _branchDelays;
    private int _delayTargetLatency;
    private MediaTime? _lastDelayRangeEnd;

    private readonly record struct BranchAlignment(int DelaySamples, int TrailingSilence);

    // SoundGroup supplies every branch end, so the mixer knows how much of each tail was consumed inline.
    internal bool TracksCompensatedBranchTails => _branchDelays is { Count: > 0 } && _branchEndTimes.Count == Inputs.Count;

    internal override double? GetFiniteSourceEndSample(int sampleRate)
    {
        int target = GetCompensationTarget(sampleRate);
        if (target == 0)
            return base.GetFiniteSourceEndSample(sampleRate);

        double end = double.NegativeInfinity;
        for (int i = 0; i < Inputs.Count; i++)
        {
            AudioNode input = Inputs[i];
            if (input.GetFiniteSourceEndSample(sampleRate) is not { } inputEnd)
                return null;

            int? latency = input.GetFixedLatencySamples(sampleRate);
            end = Math.Max(end, inputEnd + (latency is { } fixedLatency ? target - fixedLatency : 0));
        }

        return end;
    }

    internal int GetBranchCompensationSamples(AudioNode input, int sampleRate)
        => _branchDelays?.TryGetValue(input, out BranchDelay? delay) == true
            ? AudioLatency.ScaleSampleCount(delay.DelaySamples, delay.SampleRate, sampleRate)
            : 0;

    private int GetCompensationTarget(int sampleRate)
    {
        if (Inputs.Count < 2)
            return 0;

        int min = int.MaxValue;
        int max = 0;
        for (int i = 0; i < Inputs.Count; i++)
        {
            if (Inputs[i].GetFixedLatencySamples(sampleRate) is not { } latency)
                continue;

            min = Math.Min(min, latency);
            max = Math.Max(max, latency);
        }

        // Include ended branches so their siblings retain the same delay throughout the mix.
        return min < max ? max : 0;
    }

    private BranchDelay? GetBranchDelay(AudioNode input, int delaySamples, AudioBuffer output, bool hasInput)
    {
        if (_branchDelays?.TryGetValue(input, out BranchDelay? existing) == true)
        {
            if (existing.DelaySamples == delaySamples && existing.SampleRate == output.SampleRate
                && existing.ChannelCount == output.ChannelCount)
            {
                return existing;
            }

            _branchDelays.Remove(input);
        }

        if (delaySamples <= 0 || !hasInput || output.SampleCount == 0)
            return null;

        var delay = new BranchDelay(output.SampleRate, output.ChannelCount, delaySamples);
        (_branchDelays ??= new(ReferenceEqualityComparer.Instance)).Add(input, delay);
        return delay;
    }

    private sealed class BranchDelay(int sampleRate, int channelCount, int delaySamples)
    {
        private readonly float[] _history = new float[checked(channelCount * delaySamples)];
        private int _position;

        public int SampleRate { get; } = sampleRate;
        public int ChannelCount { get; } = channelCount;
        public int DelaySamples { get; } = delaySamples;
        public int PendingSamples { get; private set; }

        public void MixChannel(int channel, ReadOnlySpan<float> input, Span<float> output, float gain)
        {
            Span<float> history = _history.AsSpan(channel * DelaySamples, DelaySamples);
            int position = _position;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] += history[position] * gain;
                history[position] = input.IsEmpty ? 0f : input[i];
                if (++position == history.Length)
                    position = 0;
            }
        }

        public void Advance(int sampleCount, int trailingSilence)
        {
            _position = (int)(((long)_position + sampleCount) % DelaySamples);
            PendingSamples = trailingSilence < sampleCount
                ? Math.Max(0, DelaySamples - trailingSilence)
                : Math.Max(0, PendingSamples - sampleCount);
        }
    }
}
