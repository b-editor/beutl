using System.Text;
using Beutl.Audio;
using Beutl.Editor.Models;
using Beutl.Language;
using Beutl.Media.Decoding;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal readonly record struct SpeechWaveChunkResult(
    int SourceSampleCount,
    int OutputSampleCount,
    TimeSpan UploadedDuration);

internal static class SpeechWaveEncoder
{
    private const int WaveHeaderLength = 44;

    internal static SpeechWaveChunkResult WriteSpeechWave(
        MediaReader reader,
        int startSample,
        int requestedSamples,
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        if (startSample < 0)
            throw new ArgumentOutOfRangeException(nameof(startSample));
        if (requestedSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedSamples));
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The destination stream must be writable and seekable.",
                nameof(stream));
        }

        int sourceRate = reader.AudioInfo.SampleRate;
        if (sourceRate <= 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        const int outputRate = 16_000;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteWaveHeader(writer, outputRate);

        int sourceSamplesWritten = 0;
        int outputSamplesWritten = 0;
        int decodeBlockSize = Math.Max(1, Math.Min(sourceRate, requestedSamples));
        double nextOutputSourcePosition = 0;
        while (sourceSamplesWritten < requestedSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(decodeBlockSize, requestedSamples - sourceSamplesWritten);
            if (!reader.ReadAudio(
                    checked(startSample + sourceSamplesWritten),
                    length,
                    out AudioBuffer? audio))
            {
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
            }

            using (audio)
            {
                AudioChannelLayout layout = AudioChannelLayout.ForChannelCount(
                    reader.AudioInfo.ChannelLayout, audio.ChannelCount);
                var matrix = AudioChannelMatrix.Create(layout, AudioChannelLayout.Stereo);
                using var converted = matrix.IsIdentity ? null : matrix.Apply(audio);
                AudioBuffer stereo = converted ?? audio;

                int decodedSamples = Math.Min(audio.SampleCount, length);
                if (decodedSamples == 0)
                    break;

                Span<float> left = stereo.GetChannelData(0)[..decodedSamples];
                Span<float> right = stereo.GetChannelData(1)[..decodedSamples];
                double blockEnd = sourceSamplesWritten + decodedSamples;
                while (nextOutputSourcePosition < blockEnd)
                {
                    int sourceIndex = Math.Clamp(
                        (int)Math.Floor(nextOutputSourcePosition - sourceSamplesWritten),
                        0,
                        decodedSamples - 1);
                    double mono = (left[sourceIndex] + right[sourceIndex]) / 2d;
                    mono = double.IsFinite(mono) ? Math.Clamp(mono, -1, 1) : 0;
                    writer.Write((short)Math.Round(mono * short.MaxValue));
                    outputSamplesWritten++;
                    nextOutputSourcePosition = outputSamplesWritten * (double)sourceRate / outputRate;
                }
                sourceSamplesWritten += decodedSamples;
                if (decodedSamples < length)
                    break;
            }
        }

        if (sourceSamplesWritten == 0 || outputSamplesWritten == 0)
            throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

        int dataLength = checked(outputSamplesWritten * sizeof(short));
        PatchWaveLengths(stream, writer, dataLength);
        return new SpeechWaveChunkResult(
            sourceSamplesWritten,
            outputSamplesWritten,
            TimeSpan.FromSeconds(outputSamplesWritten / (double)outputRate));
    }

    // 16-bit mono PCM. Both lengths are written as zero and patched once the data is complete.
    private static void WriteWaveHeader(BinaryWriter writer, int sampleRate)
    {
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(0);
    }

    // Leaves the stream positioned at the end of the data.
    private static void PatchWaveLengths(Stream stream, BinaryWriter writer, int dataLength)
    {
        stream.Position = 4;
        writer.Write(checked(WaveHeaderLength - 8 + dataLength));
        stream.Position = 40;
        writer.Write(dataLength);
        stream.Position = WaveHeaderLength + dataLength;
    }

    /// <summary>
    /// Writes one 16-bit mono PCM wave from composed slices, as a whole upload chunk cannot be composed at
    /// once. The resampling position carries across slices, so a boundary neither drops nor shifts a sample.
    /// </summary>
    internal sealed class SpeechWaveWriter(Stream stream)
    {
        private const int MaximumOutputRate = 16_000;
        private readonly Stream _stream = stream;
        private int _sourceRate;
        private int _outputRate;
        private long _sourceFramesWritten;
        private double _nextOutputSourcePosition;
        private int _outputSampleCount;

        public TimeSpan UploadedDuration => _outputRate == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(_outputSampleCount / (double)_outputRate);

        public void Append(AudioFrameSnapshot snapshot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.SampleRate <= 0 || snapshot.ChannelCount <= 0 || snapshot.SampleCount <= 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            if (_sourceRate == 0)
            {
                _sourceRate = snapshot.SampleRate;
                _outputRate = Math.Min(_sourceRate, MaximumOutputRate);
                WriteHeader();
            }
            else if (snapshot.SampleRate != _sourceRate)
            {
                // Resampling assumes one rate for the whole wave; a slice at another
                // rate would land its samples at the wrong time.
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
            }

            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            long sliceStart = _sourceFramesWritten;
            long sliceEnd = sliceStart + snapshot.SampleCount;
            while (_nextOutputSourcePosition < sliceEnd)
            {
                if ((_outputSampleCount & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int frame = (int)Math.Clamp(
                    (long)Math.Floor(_nextOutputSourcePosition) - sliceStart,
                    0,
                    snapshot.SampleCount - 1);
                double mono = 0;
                for (int channel = 0; channel < snapshot.ChannelCount; channel++)
                {
                    float sample = snapshot.Interleaved[(frame * snapshot.ChannelCount) + channel];
                    mono += float.IsFinite(sample) ? sample : 0;
                }

                mono = Math.Clamp(mono / snapshot.ChannelCount, -1, 1);
                writer.Write((short)Math.Round(mono * short.MaxValue));
                _outputSampleCount++;
                _nextOutputSourcePosition = _outputSampleCount * (double)_sourceRate / _outputRate;
            }

            _sourceFramesWritten = sliceEnd;
        }

        public void Complete()
        {
            if (_outputSampleCount == 0)
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

            int dataLength = checked(_outputSampleCount * sizeof(short));
            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            PatchWaveLengths(_stream, writer, dataLength);
        }

        private void WriteHeader()
        {
            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            WriteWaveHeader(writer, _outputRate);
        }
    }

}
