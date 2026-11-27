using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

internal sealed class DuckingNode : AudioNode
{
    public const float DefaultThresholdDb = -24f;
    public const float DefaultReductionDb = 12f;
    public const float DefaultAttackMs = 30f;
    public const float DefaultReleaseMs = 250f;
    public const float MinThresholdDb = -80f;
    public const float MaxReductionDb = 60f;
    public const float MaxAttackMs = 1000f;
    public const float MaxReleaseMs = 5000f;

    private MediaTime? _lastEnd;
    private double _amount;
    private long _quietSamples;

    public required Guid[] SourceLayerIds { get; init; }
    public required MediaTime ClipStart { get; init; }
    public required IProperty<float> Threshold { get; init; }
    public required IProperty<float> Reduction { get; init; }
    public required IProperty<float> Attack { get; init; }
    public required IProperty<float> Release { get; init; }

    // The composer sets this only while processing this layer's outputs for a block.
    internal SidechainBlock? Sidechain { get; set; }

    internal sealed record SidechainBlock(MediaTime Start, int SampleRate, IReadOnlyDictionary<Guid, AudioBuffer> Layers);

    public override AudioBuffer Process(AudioProcessContext context)
        => RecordProcessedOutput(ProcessTail(Inputs[0].Process(context), context, draining: false));

    protected override AudioBuffer ProcessTail(AudioBuffer input, AudioProcessContext context, bool draining)
    {
        try
        {
            if (_lastEnd is not { } previous || !context.ContinuesFrom(previous))
            {
                _amount = 0;
                _quietSamples = 0;
            }

            _lastEnd = context.TimeRange.End;
            if (Sidechain is null && _amount == 0)
                return input;

            ApplyEnvelope(input, context);
            return input;
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private void ApplyEnvelope(AudioBuffer buffer, AudioProcessContext context)
    {
        const int chunkSize = 512;
        Span<float> peaks = stackalloc float[chunkSize];
        Span<float> thresholds = stackalloc float[chunkSize];
        Span<float> reductions = stackalloc float[chunkSize];
        Span<float> attacks = stackalloc float[chunkSize];
        Span<float> releases = stackalloc float[chunkSize];
        Span<float> samples = buffer.GetRawSpan();
        var settings = new EnvelopeSettings();

        for (int offset = 0; offset < buffer.SampleCount; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, buffer.SampleCount - offset);
            TimeRange range = context.GetChunkRange(offset, count);
            int sampleRate = context.SampleRate;
            context.AnimationSampler.SampleBuffer(Threshold, range, sampleRate, thresholds[..count]);
            context.AnimationSampler.SampleBuffer(Reduction, range, sampleRate, reductions[..count]);
            context.AnimationSampler.SampleBuffer(Attack, range, sampleRate, attacks[..count]);
            context.AnimationSampler.SampleBuffer(Release, range, sampleRate, releases[..count]);
            ReadPeaks(peaks[..count], ClipStart + range.Start, sampleRate);

            for (int i = 0; i < count; i++)
            {
                settings.Update(thresholds[i], reductions[i], attacks[i], releases[i], sampleRate);
                if (peaks[i] > settings.Threshold)
                {
                    _quietSamples = 0;
                    _amount = Math.Min(1, _amount + settings.AttackStep);
                }
                else if (_amount > 0)
                {
                    // Keep the detector open briefly across zero crossings. The hold is part of the
                    // release time, so a silent source still reaches unity within the configured time.
                    if (_quietSamples < settings.HoldSamples)
                    {
                        _quietSamples++;
                        _amount = Math.Min(1, _amount + settings.AttackStep);
                    }
                    else
                    {
                        _amount = Math.Max(0, _amount - settings.ReleaseStep);
                    }
                }

                float gain = (float)(1 - _amount * settings.Reduction);
                if (gain == 1f) continue;

                for (int channel = 0; channel < buffer.ChannelCount; channel++)
                {
                    samples[channel * buffer.SampleCount + offset + i] *= gain;
                }
            }
        }
    }

    private void ReadPeaks(Span<float> peaks, MediaTime start, int sampleRate)
    {
        peaks.Clear();
        if (Sidechain is not { } block) return;

        long firstSample = (start - block.Start).ToSamples(block.SampleRate, MidpointRounding.ToZero);
        double step = (double)block.SampleRate / sampleRate;
        foreach (Guid sourceId in SourceLayerIds)
        {
            if (!block.Layers.TryGetValue(sourceId, out AudioBuffer? source)) continue;

            ReadOnlySpan<float> sourcePeaks = source.GetChannelData(0);
            for (int i = 0; i < peaks.Length; i++)
            {
                long index = firstSample + (long)(i * step);
                if ((ulong)index < (ulong)sourcePeaks.Length)
                {
                    peaks[i] = Math.Max(peaks[i], sourcePeaks[(int)index]);
                }
            }
        }
    }

    private struct EnvelopeSettings()
    {
        private float _thresholdDb = float.NaN;
        private float _reductionDb = float.NaN;
        private float _attackMs = float.NaN;
        private float _releaseMs = float.NaN;

        public float Threshold { get; private set; }
        public double Reduction { get; private set; }
        public double AttackStep { get; private set; }
        public double ReleaseStep { get; private set; }
        public long HoldSamples { get; private set; }

        public void Update(float threshold, float reduction, float attack, float release, int sampleRate)
        {
            if (_thresholdDb != threshold)
            {
                _thresholdDb = threshold;
                Threshold = AudioMath.ConvertDbToLinear(Sanitize(threshold, DefaultThresholdDb, MinThresholdDb, 0));
            }
            if (_reductionDb != reduction)
            {
                _reductionDb = reduction;
                Reduction = 1d - AudioMath.ConvertDbToLinear(-Sanitize(reduction, DefaultReductionDb, 0, MaxReductionDb));
            }
            if (_attackMs != attack)
            {
                _attackMs = attack;
                double attackSamples = Math.Ceiling(Sanitize(attack, DefaultAttackMs, 0, MaxAttackMs) * (sampleRate / 1000d));
                AttackStep = 1d / Math.Max(1, attackSamples);
            }
            if (_releaseMs != release)
            {
                _releaseMs = release;
                long releaseSamples = (long)Math.Ceiling(Sanitize(release, DefaultReleaseMs, 0, MaxReleaseMs) * (sampleRate / 1000d));
                HoldSamples = Math.Min(sampleRate / 200, releaseSamples / 4);
                ReleaseStep = 1d / Math.Max(1, releaseSamples - HoldSamples);
            }
        }

        private static float Sanitize(float value, float fallback, float min, float max)
            => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }
}
