using System;
using System.Linq;

using BEditor.Media.Decoding;

namespace BEditor.Extensions.FFmpeg.Decoder
{
    public sealed class InputContainer : IInputContainer
    {
        private readonly FFMediaToolkit.Decoding.MediaFile _media;

        public InputContainer(string file, MediaOptions options)
        {
            if (options.StreamsToLoad is MediaMode.Video or MediaMode.AudioVideo)
            {
                _media = FFMediaToolkit.Decoding.MediaFile.Open(file, new()
                {
                    StreamsToLoad = FFMediaToolkit.Decoding.MediaMode.Video,
                    VideoPixelFormat = FFMediaToolkit.Graphics.ImagePixelFormat.Bgra32
                });

                Video = _media.VideoStreams.Select(i => new VideoStream(i)).ToArray();
            }
            else
            {
                Video = Array.Empty<IVideoStream>();
            }

            if (options.StreamsToLoad is MediaMode.Audio or MediaMode.AudioVideo)
            {
                var audio = new AudioStream(file, options);
                Audio = new[] { audio };

                _media ??= audio._media;
            }
            else
            {
                Audio = Array.Empty<IAudioStream>();
            }

            Info = new(file, _media!.Info.ContainerFormat, _media.Info.Bitrate, _media.Info.Duration, _media.Info.StartTime, new()
            {
                Title = _media.Info.Metadata.Title,
                Author = _media.Info.Metadata.Author,
                Album = _media.Info.Metadata.Album,
                Year = _media.Info.Metadata.Year,
                Genre = _media.Info.Metadata.Genre,
                Description = _media.Info.Metadata.Description,
                Language = _media.Info.Metadata.Language,
                Copyright = _media.Info.Metadata.Copyright,
                Rating = _media.Info.Metadata.Rating,
                TrackNumber = _media.Info.Metadata.TrackNumber,
            });
        }

        public MediaInfo Info { get; }

        public IVideoStream[] Video { get; }

        public IAudioStream[] Audio { get; }

        public void Dispose()
        {
            _media.Dispose();

            foreach (var video in Video)
            {
                video.Dispose();
            }

            foreach (var audio in Audio)
            {
                audio.Dispose();
            }
        }
    }
}