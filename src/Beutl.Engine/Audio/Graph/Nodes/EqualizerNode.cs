using Beutl.Audio.Effects.Equalizer;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// Audio node that performs equalizer processing.
/// </summary>
public sealed class EqualizerNode : AudioNode
{
    // Filters per band and per channel [bandIndex][channelIndex]
    private BiQuadFilter[][]? _filters;
    private int _lastSampleRate;
    private int _lastChannelCount;
    private int _lastBandCount;
    private TimeSpan? _lastTimeRangeEnd;

    /// <summary>
    /// List of equalizer bands.
    /// </summary>
    public required IReadOnlyList<EqualizerBand> Bands { get; init; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Equalizer node requires exactly one input.");

        var input = Inputs[0].Process(context);

        // Pass-through if no bands
        if (Bands.Count == 0)
        {
            return input;
        }

        // Initialize or reinitialize filters
        if (_filters == null || _lastSampleRate != context.SampleRate ||
            _lastChannelCount != input.ChannelCount || _lastBandCount != Bands.Count)
        {
            InitializeFilters(context.SampleRate, input.ChannelCount);
            _lastSampleRate = context.SampleRate;
            _lastChannelCount = input.ChannelCount;
            _lastBandCount = Bands.Count;
        }

        // Reset whenever the chunk does not continue directly from the previous one, because the
        // node instance is cached across Compose() calls and stale IIR state would otherwise bleed
        // into the first samples after a seek or stop/restart.
        if (!_lastTimeRangeEnd.HasValue || _lastTimeRangeEnd.Value != context.TimeRange.Start)
        {
            Reset();
        }
        _lastTimeRangeEnd = context.TimeRange.Start + context.TimeRange.Duration;

        // Check if any band has an actual animation assigned
        bool hasAnimation = Bands.Any(band =>
            band.Frequency?.Animation != null ||
            band.Gain?.Animation != null ||
            band.Q?.Animation != null);

        if (!hasAnimation)
        {
            return ProcessStatic(input, context);
        }

        return ProcessAnimated(input, context);
    }

    private void InitializeFilters(int sampleRate, int channelCount)
    {
        // Clear existing filters
        _filters = new BiQuadFilter[Bands.Count][];
        for (int bandIndex = 0; bandIndex < Bands.Count; bandIndex++)
        {
            _filters[bandIndex] = new BiQuadFilter[channelCount];
            for (int ch = 0; ch < channelCount; ch++)
            {
                _filters[bandIndex][ch] = new BiQuadFilter();
            }
        }
    }

    private AudioBuffer ProcessStatic(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        // Calculate coefficients for each band
        for (int bandIndex = 0; bandIndex < Bands.Count; bandIndex++)
        {
            var band = Bands[bandIndex];
            var filterType = band.FilterType?.CurrentValue ?? BiQuadFilterType.Peak;
            float frequency = band.Frequency?.CurrentValue ?? 1000f;
            float gain = band.Gain?.CurrentValue ?? 0f;
            float q = band.Q?.CurrentValue ?? 1f;

            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                _filters![bandIndex][ch].CalculateCoefficients(filterType, frequency, q, gain, context.SampleRate);
            }
        }

        // Process each channel
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);

            // Process first band
            var firstFilter = _filters![0][ch];
            for (int i = 0; i < input.SampleCount; i++)
            {
                outData[i] = firstFilter.Process(inData[i]);
            }

            // Cascade remaining bands
            for (int bandIndex = 1; bandIndex < Bands.Count; bandIndex++)
            {
                var filter = _filters[bandIndex][ch];
                for (int i = 0; i < input.SampleCount; i++)
                {
                    outData[i] = filter.Process(outData[i]);
                }
            }
        }

        // Clip protection is the master limiter's job (Composer.ApplyMasterEffects).
        // Applying it here would also compress hot input that the EQ itself did not amplify.
        return output;
    }

    private AudioBuffer ProcessAnimated(AudioBuffer input, AudioProcessContext context)
    {
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        // Buffers for chunk processing
        const int maxChunkSize = 1024;
        Span<float> frequencies = stackalloc float[Math.Min(maxChunkSize, input.SampleCount)];
        Span<float> gains = stackalloc float[Math.Min(maxChunkSize, input.SampleCount)];
        Span<float> qs = stackalloc float[Math.Min(maxChunkSize, input.SampleCount)];

        int processed = 0;
        while (processed < input.SampleCount)
        {
            int chunkSize = Math.Min(maxChunkSize, input.SampleCount - processed);

            var chunkStart = context.GetTimeForSample(processed);
            var chunkEnd = context.GetTimeForSample(processed + chunkSize);
            var chunkRange = new TimeRange(chunkStart, chunkEnd - chunkStart);

            // Process each channel
            for (int ch = 0; ch < input.ChannelCount; ch++)
            {
                var inData = input.GetChannelData(ch).Slice(processed, chunkSize);
                var outData = output.GetChannelData(ch).Slice(processed, chunkSize);

                // Copy input first
                inData.CopyTo(outData);

                // Cascade each band
                for (int bandIndex = 0; bandIndex < Bands.Count; bandIndex++)
                {
                    var band = Bands[bandIndex];
                    var filter = _filters![bandIndex][ch];
                    var filterType = band.FilterType?.CurrentValue ?? BiQuadFilterType.Peak;

                    // Sample animation values
                    context.AnimationSampler.SampleBuffer(band.Frequency, chunkRange, context.SampleRate, frequencies[..chunkSize]);
                    context.AnimationSampler.SampleBuffer(band.Gain, chunkRange, context.SampleRate, gains[..chunkSize]);
                    context.AnimationSampler.SampleBuffer(band.Q, chunkRange, context.SampleRate, qs[..chunkSize]);

                    // Process each sample
                    for (int i = 0; i < chunkSize; i++)
                    {
                        // Update coefficients
                        filter.CalculateCoefficients(filterType, frequencies[i], qs[i], gains[i], context.SampleRate);
                        // Apply filter
                        outData[i] = filter.Process(outData[i]);
                    }
                }
            }

            processed += chunkSize;
        }

        // Clip protection is the master limiter's job (Composer.ApplyMasterEffects).
        // Applying it here would also compress hot input that the EQ itself did not amplify.
        return output;
    }

    /// <summary>
    /// Resets the filter state.
    /// </summary>
    public void Reset()
    {
        if (_filters != null)
        {
            foreach (var bandFilters in _filters)
            {
                foreach (var filter in bandFilters)
                {
                    filter.Reset();
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _filters = null;
        }

        base.Dispose(disposing);
    }
}
