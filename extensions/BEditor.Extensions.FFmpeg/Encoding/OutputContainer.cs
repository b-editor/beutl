using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.Encoding;

namespace BEditor.Media.FFmpeg.Encoding
{
    public class OutputContainer : IOutputContainer
    {
        private readonly List<VideoOutputStream> _video = new();
        private readonly List<AudioOutputStream> _audio = new();
        private readonly FFMediaToolkit.Encoding.MediaBuilder _builder;
        private FFMediaToolkit.Encoding.MediaOutput? _output;

        public OutputContainer(string file)
        {
            File = file;
            _builder = FFMediaToolkit.Encoding.MediaBuilder.CreateContainer(File);
        }

        public string File { get; }

        public IEnumerable<IVideoOutputStream> Video => _video;

        public IEnumerable<IAudioOutputStream> Audio => _audio;

        public void AddAudioStream(AudioEncoderSettings config)
        {
            _builder.WithAudio(new(config.SampleRate, config.Channels)
            {
                Bitrate = config.Bitrate
            });
        }

        public void AddVideoStream(VideoEncoderSettings config)
        {
            _builder.WithVideo(new(config.VideoWidth, config.VideoHeight, config.Framerate)
            {
                Bitrate = config.Bitrate,
                KeyframeRate = config.KeyframeRate
            });
        }

        public MediaOutput Create()
        {
            _output = _builder.Create();

            foreach (var video in _output.VideoStreams)
            {
                _video.Add(new(video, new(video.Configuration.VideoWidth, video.Configuration.VideoHeight, video.Configuration.Framerate)
                {
                    Bitrate = video.Configuration.Bitrate,
                    KeyframeRate = video.Configuration.KeyframeRate
                }));
            }

            foreach (var audio in _output.AudioStreams)
            {
                _audio.Add(new(audio, new(audio.Configuration.SampleRate, audio.Configuration.Channels)
                {
                    Bitrate = audio.Configuration.Bitrate
                }));
            }

            return new(this);
        }

        public void Dispose()
        {
            _output?.Dispose();
        }

        public AudioEncoderSettings GetDefaultAudioSettings()
        {
            throw new NotImplementedException();
        }

        public VideoEncoderSettings GetDefaultVideoSettings()
        {
            throw new NotImplementedException();
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
