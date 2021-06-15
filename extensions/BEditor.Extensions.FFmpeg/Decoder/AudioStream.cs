using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.PCM;

using FFMediaToolkit.Audio;

namespace BEditor.Extensions.FFmpeg.Decoder
{
    public sealed class AudioStream : IAudioStream
    {
        internal readonly FFMediaToolkit.Decoding.MediaFile _media;
        private readonly string _tmpfile;
        private readonly FFMediaToolkit.Decoding.AudioStream _stream;

        public AudioStream(string file, MediaOptions options)
        {
            _tmpfile = Path.ChangeExtension(Path.GetTempFileName(), "mp3");
            var process = Process.Start(new ProcessStartInfo(FFmpegExecutable.GetExecutable(), $"-i \"{file}\" -vcodec copy -ar {options.SampleRate} \"{_tmpfile}\"")
            {
                CreateNoWindow = true,
            })!;
            process.WaitForExit();

            _media = FFMediaToolkit.Decoding.MediaFile.Open(_tmpfile, new() { StreamsToLoad = FFMediaToolkit.Decoding.MediaMode.Audio });
            _stream = _media.Audio;
            Info = new(_stream.Info.CodecName, MediaType.Audio, _stream.Info.Duration - _stream.Info.StartTime ?? default, _stream.Info.SampleRate, _stream.Info.NumChannels);
        }

        public AudioStreamInfo Info { get; }

        public void Dispose()
        {
            _media.Dispose();

            if (File.Exists(_tmpfile)) File.Delete(_tmpfile);
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
                sound.SetChannelData(1, array[1]);
            }
            else
            {
                sound.SetChannelData(1, array[0]);
            }

            return sound;
        }
    }
}