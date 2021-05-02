using System;
using System.IO;

using BEditor.Media.Common;
using BEditor.Media.Common.Internal;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding.Internal
{
    /// <summary>
    /// Represents the multimedia file container.
    /// </summary>
    internal sealed unsafe class InputContainer : Wrapper<AVFormatContext>
    {
        private static avio_alloc_context_read_packet? _readCallback;
        private static avio_alloc_context_seek? _seekCallback;

        private InputContainer(AVFormatContext* formatContext, int bufferSizeLimit) : base(formatContext)
        {
            Decoders = new Decoder[Pointer->nb_streams];
            MaxBufferSize = bufferSizeLimit;
        }

        private delegate void AVFormatContextDelegate(AVFormatContext* context);

        /// <summary>
        /// List of all stream codecs that have been opened from the file.
        /// </summary>
        public Decoder?[] Decoders { get; }

        /// <summary>
        /// Gets the memory limit of packets stored in the decoder's buffer.
        /// </summary>
        public int MaxBufferSize { get; }

        /// <summary>
        /// Opens a media container and stream codecs from given path.
        /// </summary>
        /// <param name="path">A path to the multimedia file.</param>
        /// <param name="options">The media settings.</param>
        /// <returns>A new instance of the <see cref="InputContainer"/> class.</returns>
        public static InputContainer LoadFile(string path, MediaOptions options)
        {
            return MakeContainer(path, options, _ => { });
        }

        /// <summary>
        /// Opens a media container and stream codecs from given stream.
        /// </summary>
        /// <param name="stream">A stream of the multimedia file.</param>
        /// <param name="options">The media settings.</param>
        /// <returns>A new instance of the <see cref="InputContainer"/> class.</returns>
        public static InputContainer LoadStream(Stream stream, MediaOptions options)
        {
            return MakeContainer(null, options, context =>
            {
                var avioStream = new AvioStream(stream);

                // Prevents garbage collection
                _readCallback = avioStream.Read;
                _seekCallback = avioStream.Seek;

                const int bufferLength = 4096;
                var avioBuffer = (byte*)ffmpeg.av_malloc(bufferLength);

                context->pb = ffmpeg.avio_alloc_context(avioBuffer, bufferLength, 0, null, _readCallback, null, _seekCallback);
                if (context->pb == null)
                {
                    throw new FFmpegException("Cannot allocate AVIOContext.");
                }
            });
        }

        /// <summary>
        /// Seeks all streams in the container to the first key frame before the specified time stamp.
        /// </summary>
        /// <param name="targetTs">The target time stamp in a stream time base.</param>
        /// <param name="streamIndex">The stream index. It will be used only to get the correct time base value.</param>
        public void SeekFile(long targetTs, int streamIndex)
        {
            ffmpeg.av_seek_frame(Pointer, streamIndex, targetTs, ffmpeg.AVSEEK_FLAG_BACKWARD).ThrowIfError($"Seek to {targetTs} failed.");

            Decoders[streamIndex]?.FlushUnmanagedBuffers();
            GetPacketFromStream(streamIndex);
        }

        /// <summary>
        /// Reads a packet from the specified stream index and buffers it in the respective codec.
        /// </summary>
        /// <param name="streamIndex">Index of the stream to read from.</param>
        public void GetPacketFromStream(int streamIndex)
        {
            MediaPacket? packet;
            do
            {
                packet = ReadPacket();
                var stream = Decoders[packet.StreamIndex];
                if (stream is null)
                {
                    packet.Wipe();
                    packet.Dispose();
                    packet = null;
                }
                else
                {
                    stream.BufferPacket(packet);
                }
            }
            while (packet?.StreamIndex != streamIndex);
        }

        /// <inheritdoc/>
        protected override void OnDisposing()
        {
            foreach (var decoder in Decoders)
            {
                decoder?.Dispose();
            }

            var ptr = Pointer;
            ffmpeg.avformat_close_input(&ptr);
        }

        private static InputContainer MakeContainer(string? url, MediaOptions options, AVFormatContextDelegate contextDelegate)
        {
            FFmpegLoader.LoadFFmpeg();

            var context = ffmpeg.avformat_alloc_context();
            options.DemuxerOptions.ApplyFlags(context);
            var dict = new FFDictionary(options.DemuxerOptions.PrivateOptions, false).Pointer;

            contextDelegate(context);

            ffmpeg.avformat_open_input(&context, url, null, &dict)
                .ThrowIfError("An error occurred while opening the file");

            ffmpeg.avformat_find_stream_info(context, null)
                .ThrowIfError("Cannot find stream info");

            var container = new InputContainer(context, options.PacketBufferSizeLimit);
            container.OpenStreams(options);
            return container;
        }

        /// <summary>
        /// Opens the streams in the file using the specified <see cref="MediaOptions"/>.
        /// </summary>
        /// <param name="options">The <see cref="MediaOptions"/> object.</param>
        private void OpenStreams(MediaOptions options)
        {
            for (var i = 0; i < Pointer->nb_streams; i++)
            {
                var stream = Pointer->streams[i];
                if (!options.ShouldLoadStreamsOfType(stream->codec->codec_type)) continue;

                try
                {
                    Decoders[i] = DecoderFactory.OpenStream(this, options, stream);
                    GetPacketFromStream(i);
                }
                catch (Exception)
                {
                    Decoders[i] = null;
                }
            }
        }

        /// <summary>
        /// Reads the next packet from this file.
        /// </summary>
        private MediaPacket ReadPacket()
        {
            var pkt = MediaPacket.AllocateEmpty();
            var result = ffmpeg.av_read_frame(Pointer, pkt.Pointer); // Gets the next packet from the file.

            // Check if the end of file error occurred
            if (result == ffmpeg.AVERROR_EOF)
            {
                throw new EndOfStreamException("End of the file.");
            }
            else
            {
                result.ThrowIfError("Cannot read next packet from the file");
            }

            return pkt;
        }
    }
}