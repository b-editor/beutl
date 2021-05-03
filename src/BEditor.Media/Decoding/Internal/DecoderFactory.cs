using BEditor.Media.Common;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding.Internal
{
    /// <summary>
    /// Contains method for opening media streams.
    /// </summary>
    internal static unsafe class DecoderFactory
    {
        /// <summary>
        /// Opens the video stream with the specified index in the media container.
        /// </summary>
        /// <param name="container">The media container.</param>
        /// <param name="options">The media options.</param>
        /// <param name="stream">The stream.</param>
        /// <returns>The opened <see cref="Decoder"/>.</returns>
        internal static Decoder? OpenStream(InputContainer container, MediaOptions options, AVStream* stream)
        {
            var format = container.Pointer;
            AVCodec* codec = null;

            var index = ffmpeg.av_find_best_stream(format, stream->codec->codec_type, stream->index, -1, &codec, 0);
            index.IfError(ffmpeg.AVERROR_DECODER_NOT_FOUND, "Cannot find a codec for the specified stream.");
            if (index < 0)
            {
                return null;
            }

            var codecContext = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar)
                .ThrowIfError("Cannot open the stream codec!");
            codecContext->pkt_timebase = stream->time_base;

            var dict = new FFDictionary(options.DecoderOptions, false).Pointer;

            ffmpeg.avcodec_open2(codecContext, codec, &dict)
                .ThrowIfError("Cannot open the stream codec!");

            return new Decoder(codecContext, stream, container);
        }
    }
}