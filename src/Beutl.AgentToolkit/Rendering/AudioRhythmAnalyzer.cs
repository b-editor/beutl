using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Composition;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.AgentToolkit.Rendering;

public sealed record AudioRhythmWindow(
    double StartSeconds,
    double DurationSeconds);

public sealed record AudioRhythmAnalysis(
    int SampleRate,
    AudioRhythmWindow AnalyzedWindow,
    double EstimatedBpm,
    double Confidence,
    IReadOnlyList<double> BeatTimesSeconds,
    IReadOnlyList<double> StrongOnsetTimesSeconds);

public sealed class AudioRhythmAnalyzer
{
    private const int WindowSize = 1024;
    private const int HopSize = 512;
    private const double DefaultMinBpm = 60;
    private const double DefaultMaxBpm = 200;
    private const double MinimumPeakNovelty = 0.12;
    private const double PeakThresholdOffset = 0.10;
    private const double PeakRefractorySeconds = 0.18;
    private const double AlignmentToleranceSeconds = 0.08;

    private static readonly double[] s_hannWindow = CreateHannWindow();
    private static readonly int[] s_bitReversal = CreateBitReversal();

    public AudioRhythmAnalysis AnalyzePcm(ReadOnlySpan<float> monoSamples, int sampleRate)
    {
        return AnalyzePcm(monoSamples, sampleRate, DefaultMinBpm, DefaultMaxBpm);
    }

    public AudioRhythmAnalysis AnalyzePcm(
        ReadOnlySpan<float> monoSamples,
        int sampleRate,
        double expectedBpmMin,
        double expectedBpmMax)
    {
        return AnalyzePcm(monoSamples, sampleRate, expectedBpmMin, expectedBpmMax, 0);
    }

    public ValueTask<AudioRhythmAnalysis> AnalyzeFileAsync(
        string path,
        double? startSeconds,
        double? durationSeconds,
        double? expectedBpmMin,
        double? expectedBpmMax,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.MediaNotFound,
                $"Media file not found: {fullPath}",
                fullPath));
        }

        double normalizedStartSeconds = NormalizeStartSeconds(startSeconds);
        (double minBpm, double maxBpm) = NormalizeBpmRange(expectedBpmMin, expectedBpmMax);
        cancellationToken.ThrowIfCancellationRequested();

        var source = new SoundSource();
        source.ReadFrom(new Uri(fullPath));
        using var resource = (SoundSource.Resource)source.ToResource(new CompositionContext(TimeSpan.Zero)
        {
            DisableResourceShare = true
        });

        if (resource.MediaReader is null)
        {
            throw new CodecUnavailableException($"No audio decoder could open '{fullPath}'.");
        }

        if (!resource.MediaReader.HasAudio || resource.SampleRate <= 0)
        {
            throw new CodecUnavailableException($"'{fullPath}' has no audio stream.");
        }

        int sampleRate = resource.SampleRate;
        double sourceDurationSeconds = resource.Duration > TimeSpan.Zero
            ? resource.Duration.TotalSeconds
            : resource.MediaReader.AudioInfo.NumSamples.ToDouble() / Math.Max(1, sampleRate);
        if (sourceDurationSeconds <= 0)
        {
            throw new CodecUnavailableException($"Audio duration could not be read from '{fullPath}'.");
        }

        double availableDurationSeconds = Math.Max(0, sourceDurationSeconds - normalizedStartSeconds);
        double normalizedDurationSeconds = NormalizeDurationSeconds(durationSeconds, availableDurationSeconds);
        long startSample = checked((long)Math.Round(normalizedStartSeconds * sampleRate));
        long sampleLength = checked((long)Math.Round(normalizedDurationSeconds * sampleRate));
        if (sampleLength > int.MaxValue)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "The requested analysis window is too long for a single PCM read.",
                path,
                "Pass durationSeconds to analyze a shorter window."));
        }

        if (sampleLength <= 0)
        {
            return ValueTask.FromResult(new AudioRhythmAnalysis(
                sampleRate,
                new AudioRhythmWindow(RoundSeconds(normalizedStartSeconds), 0),
                0,
                0,
                [],
                []));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!resource.Read((int)Math.Min(startSample, int.MaxValue), (int)sampleLength, out Ref<IPcm>? soundRef))
        {
            throw new CodecUnavailableException($"Audio samples could not be read from '{fullPath}'.");
        }

        using (soundRef)
        {
            IPcm pcm = soundRef.Value;
            using Pcm<Monaural32BitFloat> mono = pcm is Pcm<Monaural32BitFloat> direct
                ? direct.Clone()
                : pcm.Convert<Monaural32BitFloat>();

            float[] samples = new float[mono.NumSamples];
            ReadOnlySpan<Monaural32BitFloat> src = mono.DataSpan;
            for (int i = 0; i < src.Length; i++)
            {
                samples[i] = src[i].Value;
            }

            cancellationToken.ThrowIfCancellationRequested();
            AudioRhythmAnalysis zeroBased = AnalyzePcm(
                samples,
                mono.SampleRate,
                minBpm,
                maxBpm,
                normalizedStartSeconds);
            return ValueTask.FromResult(zeroBased);
        }
    }

    private static AudioRhythmAnalysis AnalyzePcm(
        ReadOnlySpan<float> monoSamples,
        int sampleRate,
        double expectedBpmMin,
        double expectedBpmMax,
        double timeOffsetSeconds)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        }

        (double minBpm, double maxBpm) = NormalizeBpmRange(expectedBpmMin, expectedBpmMax);
        double durationSeconds = monoSamples.Length / (double)sampleRate;
        if (monoSamples.Length == 0)
        {
            return Empty(sampleRate, timeOffsetSeconds, durationSeconds);
        }

        int frameCount = Math.Max(1, (int)Math.Ceiling(monoSamples.Length / (double)HopSize) + 1);
        double[] energyNovelty = new double[frameCount];
        double[] fluxNovelty = new double[frameCount];
        double[] previousSpectrum = new double[WindowSize / 2];
        double[] real = new double[WindowSize];
        double[] imaginary = new double[WindowSize];

        double previousEnergy = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int frameStart = (frame * HopSize) - (WindowSize / 2);
            Array.Clear(imaginary);
            double energy = 0;
            for (int i = 0; i < WindowSize; i++)
            {
                int sampleIndex = frameStart + i;
                double sample = sampleIndex >= 0 && sampleIndex < monoSamples.Length ? monoSamples[sampleIndex] : 0;
                double windowed = sample * s_hannWindow[i];
                real[i] = windowed;
                energy += windowed * windowed;
            }

            energy = Math.Log(1 + (energy / WindowSize));
            energyNovelty[frame] = Math.Max(0, energy - previousEnergy);
            previousEnergy = energy;

            ApplyFft(real, imaginary);
            double flux = 0;
            for (int bin = 0; bin < previousSpectrum.Length; bin++)
            {
                double magnitude = Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));
                flux += Math.Max(0, magnitude - previousSpectrum[bin]);
                previousSpectrum[bin] = magnitude;
            }

            fluxNovelty[frame] = flux / previousSpectrum.Length;
        }

        double[] novelty = CombineNovelty(energyNovelty, fluxNovelty);
        List<OnsetPeak> onsets = PickOnsets(novelty, sampleRate, timeOffsetSeconds);
        if (onsets.Count == 0)
        {
            return Empty(sampleRate, timeOffsetSeconds, durationSeconds);
        }

        RhythmEstimate estimate = EstimateTempo(onsets, durationSeconds, minBpm, maxBpm, timeOffsetSeconds);
        double[] strongOnsets = onsets
            .Select(onset => RoundSeconds(onset.TimeSeconds))
            .ToArray();

        return new AudioRhythmAnalysis(
            sampleRate,
            new AudioRhythmWindow(
                RoundSeconds(timeOffsetSeconds),
                RoundSeconds(durationSeconds)),
            RoundBpm(estimate.Bpm),
            Math.Round(estimate.Confidence, 3, MidpointRounding.AwayFromZero),
            estimate.BeatTimesSeconds.Select(RoundSeconds).ToArray(),
            strongOnsets);
    }

    private static AudioRhythmAnalysis Empty(int sampleRate, double timeOffsetSeconds, double durationSeconds)
    {
        return new AudioRhythmAnalysis(
            sampleRate,
            new AudioRhythmWindow(
                RoundSeconds(timeOffsetSeconds),
                RoundSeconds(durationSeconds)),
            0,
            0,
            [],
            []);
    }

    private static double[] CombineNovelty(double[] energyNovelty, double[] fluxNovelty)
    {
        double maxEnergy = Math.Max(1e-9, energyNovelty.Max());
        double maxFlux = Math.Max(1e-9, fluxNovelty.Max());
        double[] novelty = new double[energyNovelty.Length];
        for (int i = 0; i < novelty.Length; i++)
        {
            novelty[i] = (energyNovelty[i] / maxEnergy * 0.45) + (fluxNovelty[i] / maxFlux * 0.55);
        }

        if (novelty.Length < 3)
        {
            return novelty;
        }

        double[] smoothed = new double[novelty.Length];
        smoothed[0] = novelty[0];
        smoothed[^1] = novelty[^1];
        for (int i = 1; i < novelty.Length - 1; i++)
        {
            smoothed[i] = (novelty[i - 1] + (novelty[i] * 2) + novelty[i + 1]) / 4d;
        }

        return smoothed;
    }

    private static List<OnsetPeak> PickOnsets(double[] novelty, int sampleRate, double timeOffsetSeconds)
    {
        var peaks = new List<OnsetPeak>();
        int refractoryFrames = Math.Max(1, (int)Math.Round(PeakRefractorySeconds * sampleRate / HopSize));
        int lastPeak = -refractoryFrames;
        for (int i = 0; i < novelty.Length; i++)
        {
            double threshold = LocalThreshold(novelty, i);
            double previous = i == 0 ? double.NegativeInfinity : novelty[i - 1];
            double next = i == novelty.Length - 1 ? double.NegativeInfinity : novelty[i + 1];
            bool localMaximum = novelty[i] >= previous && novelty[i] > next;
            if (!localMaximum
                || novelty[i] < threshold
                || novelty[i] < MinimumPeakNovelty
                || i - lastPeak < refractoryFrames)
            {
                continue;
            }

            double timeSeconds = timeOffsetSeconds + (i * HopSize / (double)sampleRate);
            peaks.Add(new OnsetPeak(timeSeconds, novelty[i]));
            lastPeak = i;
        }

        return peaks;
    }

    private static double LocalThreshold(double[] novelty, int index)
    {
        int start = Math.Max(0, index - 6);
        int end = Math.Min(novelty.Length - 1, index + 6);
        double sum = 0;
        double sumSq = 0;
        int count = 0;
        for (int i = start; i <= end; i++)
        {
            sum += novelty[i];
            sumSq += novelty[i] * novelty[i];
            count++;
        }

        double mean = sum / count;
        double variance = Math.Max(0, (sumSq / count) - (mean * mean));
        return mean + Math.Sqrt(variance) * 0.35 + PeakThresholdOffset;
    }

    private static RhythmEstimate EstimateTempo(
        IReadOnlyList<OnsetPeak> onsets,
        double windowDurationSeconds,
        double minBpm,
        double maxBpm,
        double timeOffsetSeconds)
    {
        if (onsets.Count < 2)
        {
            return new RhythmEstimate(0, 0, []);
        }

        const int binsPerBpm = 2;
        int minBin = (int)Math.Round(minBpm * binsPerBpm);
        int maxBin = (int)Math.Round(maxBpm * binsPerBpm);
        double[] histogram = new double[maxBin - minBin + 1];
        double totalWeight = 0;
        for (int i = 0; i < onsets.Count; i++)
        {
            for (int j = i + 1; j < onsets.Count; j++)
            {
                double interval = onsets[j].TimeSeconds - onsets[i].TimeSeconds;
                if (interval <= 0)
                {
                    continue;
                }

                for (int subdivision = 1; subdivision <= 4; subdivision++)
                {
                    double beatInterval = interval / subdivision;
                    double bpm = 60d / beatInterval;
                    if (bpm < minBpm || bpm > maxBpm)
                    {
                        continue;
                    }

                    int bin = (int)Math.Round(bpm * binsPerBpm) - minBin;
                    double weight = Math.Sqrt(onsets[i].Strength * onsets[j].Strength) / subdivision;
                    histogram[bin] += weight;
                    totalWeight += weight;
                }
            }
        }

        if (totalWeight <= 0)
        {
            return new RhythmEstimate(0, 0, []);
        }

        int bestBin = 0;
        for (int i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[bestBin])
            {
                bestBin = i;
            }
        }

        double bpmEstimate = (bestBin + minBin) / (double)binsPerBpm;
        double beatIntervalSeconds = 60d / bpmEstimate;
        double phase = SelectBeatPhase(onsets, beatIntervalSeconds, timeOffsetSeconds, windowDurationSeconds);
        double[] beatTimes = BuildBeatGrid(phase, beatIntervalSeconds, timeOffsetSeconds, windowDurationSeconds);
        double alignment = BeatAlignmentScore(onsets, beatTimes);
        double concentration = histogram[bestBin] / totalWeight;
        double confidence = Math.Clamp(
            (0.45 * Math.Min(1, onsets.Count / 8d)) + (0.35 * concentration) + (0.20 * alignment),
            0,
            1);
        return new RhythmEstimate(bpmEstimate, confidence, beatTimes);
    }

    private static double SelectBeatPhase(
        IReadOnlyList<OnsetPeak> onsets,
        double beatIntervalSeconds,
        double timeOffsetSeconds,
        double windowDurationSeconds)
    {
        double bestPhase = onsets[0].TimeSeconds;
        double bestScore = double.NegativeInfinity;
        foreach (OnsetPeak onset in onsets)
        {
            double score = ScoreBeatPhase(onsets, onset.TimeSeconds, beatIntervalSeconds, timeOffsetSeconds, windowDurationSeconds);
            if (score > bestScore)
            {
                bestScore = score;
                bestPhase = onset.TimeSeconds;
            }
        }

        return bestPhase;
    }

    private static double ScoreBeatPhase(
        IReadOnlyList<OnsetPeak> onsets,
        double phase,
        double beatIntervalSeconds,
        double timeOffsetSeconds,
        double windowDurationSeconds)
    {
        double score = 0;
        double windowEnd = timeOffsetSeconds + windowDurationSeconds;
        double first = phase;
        while (first - beatIntervalSeconds >= timeOffsetSeconds)
        {
            first -= beatIntervalSeconds;
        }

        for (double beat = first; beat <= windowEnd + 1e-6; beat += beatIntervalSeconds)
        {
            double nearest = onsets
                .Select(onset => Math.Abs(onset.TimeSeconds - beat))
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            if (nearest <= AlignmentToleranceSeconds)
            {
                score += 1 - (nearest / AlignmentToleranceSeconds);
            }
        }

        return score;
    }

    private static double[] BuildBeatGrid(
        double phase,
        double beatIntervalSeconds,
        double timeOffsetSeconds,
        double windowDurationSeconds)
    {
        var beats = new List<double>();
        double windowEnd = timeOffsetSeconds + windowDurationSeconds;
        double current = phase;
        while (current - beatIntervalSeconds >= timeOffsetSeconds)
        {
            current -= beatIntervalSeconds;
        }

        while (current <= windowEnd + 1e-6)
        {
            if (current >= timeOffsetSeconds - 1e-6)
            {
                beats.Add(current);
            }

            current += beatIntervalSeconds;
        }

        return beats.ToArray();
    }

    private static double BeatAlignmentScore(IReadOnlyList<OnsetPeak> onsets, IReadOnlyList<double> beats)
    {
        if (beats.Count == 0 || onsets.Count == 0)
        {
            return 0;
        }

        int aligned = 0;
        foreach (OnsetPeak onset in onsets)
        {
            double nearest = beats.Min(beat => Math.Abs(beat - onset.TimeSeconds));
            if (nearest <= AlignmentToleranceSeconds)
            {
                aligned++;
            }
        }

        return aligned / (double)onsets.Count;
    }

    private static void ApplyFft(double[] real, double[] imaginary)
    {
        for (int i = 0; i < WindowSize; i++)
        {
            int j = s_bitReversal[i];
            if (j <= i)
            {
                continue;
            }

            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }

        for (int length = 2; length <= WindowSize; length <<= 1)
        {
            double angle = -2d * Math.PI / length;
            double wLengthReal = Math.Cos(angle);
            double wLengthImaginary = Math.Sin(angle);
            for (int start = 0; start < WindowSize; start += length)
            {
                double wReal = 1;
                double wImaginary = 0;
                int half = length / 2;
                for (int offset = 0; offset < half; offset++)
                {
                    int even = start + offset;
                    int odd = even + half;
                    double oddReal = (real[odd] * wReal) - (imaginary[odd] * wImaginary);
                    double oddImaginary = (real[odd] * wImaginary) + (imaginary[odd] * wReal);
                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;

                    double nextReal = (wReal * wLengthReal) - (wImaginary * wLengthImaginary);
                    wImaginary = (wReal * wLengthImaginary) + (wImaginary * wLengthReal);
                    wReal = nextReal;
                }
            }
        }
    }

    private static double[] CreateHannWindow()
    {
        double[] window = new double[WindowSize];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = 0.5 - (0.5 * Math.Cos(2d * Math.PI * i / (WindowSize - 1)));
        }

        return window;
    }

    private static int[] CreateBitReversal()
    {
        int[] result = new int[WindowSize];
        int bitCount = (int)Math.Log2(WindowSize);
        for (int i = 0; i < WindowSize; i++)
        {
            int reversed = 0;
            for (int bit = 0; bit < bitCount; bit++)
            {
                reversed = (reversed << 1) | ((i >> bit) & 1);
            }

            result[i] = reversed;
        }

        return result;
    }

    private static double NormalizeStartSeconds(double? seconds)
    {
        if (seconds is null)
        {
            return 0;
        }

        if (!double.IsFinite(seconds.Value) || seconds.Value < 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "startSeconds must be a finite non-negative number.",
                seconds.Value.ToString("R")));
        }

        return seconds.Value;
    }

    private static double NormalizeDurationSeconds(double? seconds, double availableDurationSeconds)
    {
        if (seconds is null)
        {
            return availableDurationSeconds;
        }

        if (!double.IsFinite(seconds.Value) || seconds.Value <= 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "durationSeconds must be a finite positive number.",
                seconds.Value.ToString("R")));
        }

        return Math.Min(seconds.Value, availableDurationSeconds);
    }

    private static (double Min, double Max) NormalizeBpmRange(double? min, double? max)
    {
        double normalizedMin = min ?? DefaultMinBpm;
        double normalizedMax = max ?? DefaultMaxBpm;
        if (!double.IsFinite(normalizedMin) || !double.IsFinite(normalizedMax))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "expectedBpmMin and expectedBpmMax must be finite numbers."));
        }

        normalizedMin = Math.Max(DefaultMinBpm, normalizedMin);
        normalizedMax = Math.Min(DefaultMaxBpm, normalizedMax);
        if (normalizedMin > normalizedMax)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "The BPM constraint must overlap the supported 60-200 BPM range."));
        }

        return (normalizedMin, normalizedMax);
    }

    private static double RoundSeconds(double seconds)
        => Math.Round(seconds, 4, MidpointRounding.AwayFromZero);

    private static double RoundBpm(double bpm)
        => Math.Round(bpm, 2, MidpointRounding.AwayFromZero);

    private readonly record struct OnsetPeak(double TimeSeconds, double Strength);

    private sealed record RhythmEstimate(
        double Bpm,
        double Confidence,
        IReadOnlyList<double> BeatTimesSeconds);
}
