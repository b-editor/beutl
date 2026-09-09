using System.Text;
using Beutl.Editor.Models;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Services.AI;

internal readonly record struct SpeechWaveChunkResult(
    int SourceSampleCount,
    int OutputSampleCount,
    TimeSpan UploadedDuration);

internal static class SpeechWaveEncoder
{
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
        const int waveHeaderLength = 44;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(outputRate);
        writer.Write(outputRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(0);

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
                    out Ref<IPcm>? audioRef))
            {
                throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);
            }

            using (audioRef)
            {
                if (audioRef.Value is not Pcm<Stereo32BitFloat> pcm)
                    throw new SubtitleInputException(Strings.AiSubtitle_NoAudioInRange);

                int decodedSamples = Math.Min(pcm.NumSamples, length);
                if (decodedSamples == 0)
                    break;

                Span<Stereo32BitFloat> source = pcm.DataSpan[..decodedSamples];
                double blockEnd = sourceSamplesWritten + decodedSamples;
                while (nextOutputSourcePosition < blockEnd)
                {
                    int sourceIndex = Math.Clamp(
                        (int)Math.Floor(nextOutputSourcePosition - sourceSamplesWritten),
                        0,
                        decodedSamples - 1);
                    Stereo32BitFloat sample = source[sourceIndex];
                    double mono = (sample.Left + sample.Right) / 2d;
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
        stream.Position = 4;
        writer.Write(checked(waveHeaderLength - 8 + dataLength));
        stream.Position = 40;
        writer.Write(dataLength);
        stream.Position = waveHeaderLength + dataLength;
        return new SpeechWaveChunkResult(
            sourceSamplesWritten,
            outputSamplesWritten,
            TimeSpan.FromSeconds(outputSamplesWritten / (double)outputRate));
    }

    /// <summary>
    /// Writes one 16-bit mono PCM wave from as many composed slices as it is
    /// handed. The slices are separate calls because a whole upload chunk cannot
    /// be composed at once (see the composition slice duration), and the
    /// resampling position carries across them so a slice boundary neither drops
    /// a sample nor shifts the ones after it.
    /// </summary>
    internal sealed class SpeechWaveWriter(Stream stream)
    {
        private const int WaveHeaderLength = 44;
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
            _stream.Position = 4;
            writer.Write(checked(WaveHeaderLength - 8 + dataLength));
            _stream.Position = 40;
            writer.Write(dataLength);
            _stream.Position = WaveHeaderLength + dataLength;
        }

        private void WriteHeader()
        {
            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(_outputRate);
            writer.Write(_outputRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(0);
        }
    }

}
