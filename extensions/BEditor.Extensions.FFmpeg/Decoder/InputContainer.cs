using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.Decoding;

using FFMediaToolkit.Encoding;

namespace BEditor.Extensions.FFmpeg.Decoder
{
    public sealed class InputContainer : IInputContainer
    {
        private readonly FFMediaToolkit.Decoding.MediaFile _media;

        public InputContainer(string file, MediaOptions options)
        {
            _media = FFMediaToolkit.Decoding.MediaFile.Open(file, new()
            {
                StreamsToLoad = options.StreamsToLoad is MediaMode.Audio ?
                    FFMediaToolkit.Decoding.MediaMode.Audio :
                    (options.StreamsToLoad is MediaMode.Video ?
                        FFMediaToolkit.Decoding.MediaMode.Video :
                        FFMediaToolkit.Decoding.MediaMode.AudioVideo)
            });

            Info = new(file, _media.Info.ContainerFormat, _media.Info.Bitrate, _media.Info.Duration, _media.Info.StartTime, new()
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

            var video = _media.VideoStreams.Select(i => new VideoStream(i)).OfType<IMediaStream>();
            var audio = _media.AudioStreams.Select(i => new AudioStream(i)).OfType<IMediaStream>();

            Streams = video.Concat(audio).ToArray();
        }

        public IMediaStream[] Streams { get; }

        public MediaInfo Info { get; }

        public void Dispose()
        {
            _media.Dispose();
        }
    }
}
