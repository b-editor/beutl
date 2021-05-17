using System;
using System.Diagnostics.CodeAnalysis;

using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.PCM;

using FFMediaToolkit.Audio;

namespace BEditor.Extensions.FFmpeg.Decoder
{
    public sealed class AudioStream : IAudioStream
    {
        private readonly FFMediaToolkit.Decoding.AudioStream _stream;

        public AudioStream(FFMediaToolkit.Decoding.AudioStream stream)
        {
            _stream = stream;

            Info = new(stream.Info.CodecName, MediaType.Audio, stream.Info.Duration, stream.Info.SampleRate);
        }

        public AudioStreamInfo Info { get; }

        public void Dispose()
        {
            _stream.Dispose();
        }

        public Sound<StereoPCMFloat> GetFrame(TimeSpan time)
        {
            return ToSound(_stream.GetFrame(time));
        }

        public Sound<StereoPCMFloat> GetNextFrame()
        {
            return ToSound(_stream.GetNextFrame());
        }

        public bool TryGetFrame(TimeSpan time, [NotNullWhen(true)] out Sound<StereoPCMFloat>? sound)
        {
            var result = _stream.TryGetFrame(time, out var data);
            if (result)
            {
                sound = ToSound(data);
            }
            else
            {
                sound = null;
            }

            return result;
        }

        public bool TryGetNextFrame([NotNullWhen(true)] out Sound<StereoPCMFloat>? sound)
        {
            var result = _stream.TryGetNextFrame(out var data);
            if (result)
            {
                sound = ToSound(data);
            }
            else
            {
                sound = null;
            }

            return result;
        }

        private Sound<StereoPCMFloat> ToSound(AudioData data)
        {
            var sound = new Sound<StereoPCMFloat>(Info.SampleRate, data.NumSamples);
            var array = data.GetSampleData();

            sound.SetChannelData(0, array[0]);

            if (array.Length is 2)
            {
                sound.SetChannelData(0, array[0]);
            }
            else
            {
                sound.SetChannelData(1, array[1]);
            }

            return sound;
        }
    }
}
