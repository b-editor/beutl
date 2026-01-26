using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Beutl.Audio.Graph.Nodes;

/// <summary>
/// WSOLA (Waveform Similarity Overlap-Add) processor for time stretching audio without pitch change.
/// </summary>
internal sealed unsafe class WsolaProcessor : IDisposable
{
    private readonly WsolaConfig _config;
    private readonly int _sampleRate;
    private readonly int _frameSizeSamples;
    private readonly int _hopSizeSamples;
    private readonly int _searchRangeSamples;
    private readonly float[] _hannWindow;

    // Input buffer for buffering incoming samples
    private float* _inputBuffer;
    private int _inputBufferSize;
    private int _inputWritePos;
    private int _inputReadPos;

    // Output buffer for overlap-add
    private float* _outputBuffer;
    private int _outputBufferSize;

    // Processing state
    private double _analysisPosition;
    private double _synthesisPosition;
    private int _lastBestOffset;
    private bool _disposed;

    public WsolaProcessor(int sampleRate, WsolaConfig config)
    {
        _sampleRate = sampleRate;
        _config = config;

        _frameSizeSamples = config.GetFrameSizeSamples(sampleRate);
        _hopSizeSamples = config.GetHopSizeSamples(sampleRate);
        _searchRangeSamples = config.GetSearchRangeSamples(sampleRate);

        // Create Hann window
        _hannWindow = CreateHannWindow(_frameSizeSamples);

        // Allocate input buffer (enough for multiple frames + search range)
        _inputBufferSize = _frameSizeSamples * 8 + _searchRangeSamples * 2;
        _inputBuffer = (float*)NativeMemory.AllocZeroed((nuint)(_inputBufferSize * sizeof(float)));

        // Allocate output buffer
        _outputBufferSize = _frameSizeSamples * 4;
        _outputBuffer = (float*)NativeMemory.AllocZeroed((nuint)(_outputBufferSize * sizeof(float)));

        _analysisPosition = 0;
        _synthesisPosition = 0;
        _lastBestOffset = 0;
    }

    ~WsolaProcessor()
    {
        Dispose(false);
    }

    /// <summary>
    /// Process input samples and produce time-stretched output.
    /// </summary>
    /// <param name="input">Input samples</param>
    /// <param name="speed">Speed factor (1.0 = normal, &gt;1.0 = faster, &lt;1.0 = slower)</param>
    /// <param name="output">Output buffer to write to</param>
    /// <returns>Number of samples written to output</returns>
    public int Process(ReadOnlySpan<float> input, float speed, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Write input to buffer
        WriteToInputBuffer(input);

        int outputWritten = 0;
        int analysisHop = (int)(_hopSizeSamples * speed);

        while (outputWritten < output.Length)
        {
            // Check if we have enough input samples
            int availableInput = GetAvailableInputSamples();
            int requiredInput = _frameSizeSamples + _searchRangeSamples * 2;

            if (availableInput < requiredInput)
                break;

            // Find optimal offset using cross-correlation
            int bestOffset = FindBestOffset(analysisHop);

            // Extract and window the current frame
            ExtractAndOverlapAdd(bestOffset, output.Slice(outputWritten));

            // Advance positions
            _analysisPosition += analysisHop + bestOffset - _lastBestOffset;
            _synthesisPosition += _hopSizeSamples;
            _lastBestOffset = bestOffset;

            // Consume input samples
            int toConsume = Math.Min(analysisHop, availableInput);
            _inputReadPos = (_inputReadPos + toConsume) % _inputBufferSize;

            outputWritten += _hopSizeSamples;

            if (outputWritten + _hopSizeSamples > output.Length)
                break;
        }

        return outputWritten;
    }

    /// <summary>
    /// Reset processor state for seeking.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeMemory.Clear(_inputBuffer, (nuint)(_inputBufferSize * sizeof(float)));
        NativeMemory.Clear(_outputBuffer, (nuint)(_outputBufferSize * sizeof(float)));

        _inputWritePos = 0;
        _inputReadPos = 0;
        _analysisPosition = 0;
        _synthesisPosition = 0;
        _lastBestOffset = 0;
    }

    private void WriteToInputBuffer(ReadOnlySpan<float> input)
    {
        for (int i = 0; i < input.Length; i++)
        {
            _inputBuffer[_inputWritePos] = input[i];
            _inputWritePos = (_inputWritePos + 1) % _inputBufferSize;
        }
    }

    private int GetAvailableInputSamples()
    {
        int available = _inputWritePos - _inputReadPos;
        if (available < 0)
            available += _inputBufferSize;
        return available;
    }

    private int FindBestOffset(int analysisHop)
    {
        // For silent or very quiet sections, skip correlation
        float energy = CalculateFrameEnergy(_inputReadPos, _frameSizeSamples);
        if (energy < 1e-10f)
            return 0;

        int bestOffset = 0;
        float maxCorrelation = float.MinValue;

        int searchStart = -_searchRangeSamples;
        int searchEnd = _searchRangeSamples;

        for (int offset = searchStart; offset <= searchEnd; offset++)
        {
            float correlation = CalculateNormalizedCorrelation(
                _inputReadPos,
                _inputReadPos + analysisHop + offset,
                _frameSizeSamples);

            if (correlation > maxCorrelation)
            {
                maxCorrelation = correlation;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    private float CalculateFrameEnergy(int startPos, int length)
    {
        float energy = 0f;
        for (int i = 0; i < length; i++)
        {
            int idx = (startPos + i) % _inputBufferSize;
            float sample = _inputBuffer[idx];
            energy += sample * sample;
        }
        return energy;
    }

    private float CalculateNormalizedCorrelation(int pos1, int pos2, int length)
    {
        float crossCorrelation = 0f;
        float energy1 = 0f;
        float energy2 = 0f;

        if (Avx2.IsSupported && length >= 8)
        {
            return CalculateNormalizedCorrelationSimd(pos1, pos2, length);
        }

        for (int i = 0; i < length; i++)
        {
            int idx1 = (pos1 + i) % _inputBufferSize;
            int idx2 = (pos2 + i) % _inputBufferSize;

            float s1 = _inputBuffer[idx1];
            float s2 = _inputBuffer[idx2];

            crossCorrelation += s1 * s2;
            energy1 += s1 * s1;
            energy2 += s2 * s2;
        }

        float denominator = MathF.Sqrt(energy1 * energy2);
        if (denominator < 1e-10f)
            return 0f;

        return crossCorrelation / denominator;
    }

    private float CalculateNormalizedCorrelationSimd(int pos1, int pos2, int length)
    {
        var crossCorr = Vector256<float>.Zero;
        var energy1Vec = Vector256<float>.Zero;
        var energy2Vec = Vector256<float>.Zero;

        int i = 0;
        int vectorLength = length - (length % 8);

        // Temporary buffers for non-contiguous reads
        Span<float> temp1 = stackalloc float[8];
        Span<float> temp2 = stackalloc float[8];

        for (; i < vectorLength; i += 8)
        {
            // Load samples (handle wrap-around)
            for (int j = 0; j < 8; j++)
            {
                temp1[j] = _inputBuffer[(pos1 + i + j) % _inputBufferSize];
                temp2[j] = _inputBuffer[(pos2 + i + j) % _inputBufferSize];
            }

            var v1 = Vector256.Create(temp1[0], temp1[1], temp1[2], temp1[3], temp1[4], temp1[5], temp1[6], temp1[7]);
            var v2 = Vector256.Create(temp2[0], temp2[1], temp2[2], temp2[3], temp2[4], temp2[5], temp2[6], temp2[7]);

            crossCorr = Avx.Add(crossCorr, Avx.Multiply(v1, v2));
            energy1Vec = Avx.Add(energy1Vec, Avx.Multiply(v1, v1));
            energy2Vec = Avx.Add(energy2Vec, Avx.Multiply(v2, v2));
        }

        // Horizontal sum
        float crossCorrelation = HorizontalSum(crossCorr);
        float energy1 = HorizontalSum(energy1Vec);
        float energy2 = HorizontalSum(energy2Vec);

        // Process remaining samples
        for (; i < length; i++)
        {
            int idx1 = (pos1 + i) % _inputBufferSize;
            int idx2 = (pos2 + i) % _inputBufferSize;

            float s1 = _inputBuffer[idx1];
            float s2 = _inputBuffer[idx2];

            crossCorrelation += s1 * s2;
            energy1 += s1 * s1;
            energy2 += s2 * s2;
        }

        float denominator = MathF.Sqrt(energy1 * energy2);
        if (denominator < 1e-10f)
            return 0f;

        return crossCorrelation / denominator;
    }

    private static float HorizontalSum(Vector256<float> v)
    {
        var low = v.GetLower();
        var high = v.GetUpper();
        var sum128 = Vector128.Add(low, high);
        var sum64 = Vector128.Add(sum128, Vector128.Shuffle(sum128, Vector128.Create(2, 3, 0, 1)));
        var sum32 = Vector128.Add(sum64, Vector128.Shuffle(sum64, Vector128.Create(1, 0, 3, 2)));
        return sum32.ToScalar();
    }

    private void ExtractAndOverlapAdd(int offset, Span<float> output)
    {
        int frameStart = (_inputReadPos + offset + _inputBufferSize) % _inputBufferSize;

        for (int i = 0; i < _hopSizeSamples && i < output.Length; i++)
        {
            float outSample = 0f;

            // Apply windowed overlap-add
            for (int j = 0; j < _frameSizeSamples; j++)
            {
                int inputIdx = (frameStart + j) % _inputBufferSize;
                int windowIdx = j;

                float contribution = _inputBuffer[inputIdx] * _hannWindow[windowIdx];

                // Calculate where this sample contributes to output
                int outputOffset = j - i;
                if (outputOffset == 0)
                {
                    outSample += contribution;
                }
            }

            output[i] = outSample;
        }
    }

    private static float[] CreateHannWindow(int size)
    {
        float[] window = new float[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        }
        return window;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_inputBuffer != null)
            {
                NativeMemory.Free(_inputBuffer);
                _inputBuffer = null;
            }

            if (_outputBuffer != null)
            {
                NativeMemory.Free(_outputBuffer);
                _outputBuffer = null;
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
