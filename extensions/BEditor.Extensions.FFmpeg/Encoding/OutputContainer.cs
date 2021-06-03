using System.Collections.Generic;
using System.Linq;

using BEditor.Media;
using BEditor.Media.Encoding;

using AudioCodec = FFMediaToolkit.Encoding.AudioCodec;
using EncoderPreset = FFMediaToolkit.Encoding.EncoderPreset;
using ImagePixelFormat = FFMediaToolkit.Graphics.ImagePixelFormat;
using SampleFormat = FFMediaToolkit.Audio.SampleFormat;
using VideoCodec = FFMediaToolkit.Encoding.VideoCodec;

namespace BEditor.Extensions.FFmpeg.Encoding
{
    public class OutputContainer : IOutputContainer
    {
        private readonly List<VideoEncoderSettings> _videoConfig = new();
        private readonly List<AudioEncoderSettings> _audioConfig = new();
        private readonly FFMediaToolkit.Encoding.MediaBuilder _builder;
        private FFMediaToolkit.Encoding.MediaOutput? _output;

        public OutputContainer(string file)
        {
            File = file;
            _builder = FFMediaToolkit.Encoding.MediaBuilder.CreateContainer(File);
        }

        public string File { get; }

        public IEnumerable<IVideoOutputStream> Video { get; private set; } = Enumerable.Empty<IVideoOutputStream>();

        public IEnumerable<IAudioOutputStream> Audio { get; private set; } = Enumerable.Empty<IAudioOutputStream>();

        public void AddAudioStream(AudioEncoderSettings config)
        {
            _audioConfig.Add(config);
        }

        public void AddVideoStream(VideoEncoderSettings config)
        {
            _videoConfig.Add(config);
            _builder.WithVideo(new(config.VideoWidth, config.VideoHeight, config.Framerate)
            {
                Bitrate = config.Bitrate,
                KeyframeRate = config.KeyframeRate,
                VideoFormat = config.CodecOptions.TryGetValue("Format", out var fmt) ? (ImagePixelFormat)fmt : ImagePixelFormat.Bgra32,
                EncoderPreset = config.CodecOptions.TryGetValue("Preset", out var preset) ? (EncoderPreset)preset : EncoderPreset.Medium,
                Codec = config.CodecOptions.TryGetValue("Codec", out var codec) ? (VideoCodec)codec : VideoCodec.Default,
            });
        }

        public MediaOutput Create()
        {
            _output = _builder.Create();

            Video = _output.VideoStreams.Zip(_videoConfig).Select(i => new VideoOutputStream(i.First, i.Second)).ToArray();
            Audio = _audioConfig.Select(i => new AudioOutputStream(i, File)).ToArray();

            return new(this);
        }

        public void Dispose()
        {
            _output?.Dispose();

            foreach (var video in Video)
            {
                video.Dispose();
            }

            foreach (var audio in Audio)
            {
                audio.Dispose();
            }
        }

        public AudioEncoderSettings GetDefaultAudioSettings()
        {
            return new(44100, 2)
            {
                CodecOptions =
                {
                    { "Format", SampleFormat.SingleP },
                    { "Codec", AudioCodec.Default },
                }
            };
        }

        public VideoEncoderSettings GetDefaultVideoSettings()
        {
            return new(1920, 1080)
            {
                CodecOptions =
                {
                    { "Format", ImagePixelFormat.Yuv420 },
                    { "Preset", EncoderPreset.Medium },
                    { "Codec", VideoCodec.Default },
                }
            };
        }

        public void SetMetadata(ContainerMetadata metadata)
        {
            _builder.UseMetadata(new()
            {
                Title = metadata.Title,
                Author = metadata.Author,
                Album = metadata.Album,
                Year = metadata.Year,
                Genre = metadata.Genre,
                Description = metadata.Description,
                Language = metadata.Language,
                Copyright = metadata.Copyright,
                Rating = metadata.Rating,
                TrackNumber = metadata.TrackNumber,
            });
        }
    }
}