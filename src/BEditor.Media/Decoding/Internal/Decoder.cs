using System;
using System.Collections.Generic;

using BEditor.Media.Common;
using BEditor.Media.Common.Internal;
using BEditor.Media.Helpers;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoding.Internal
{
    /// <summary>
    /// Represents a input multimedia stream.
    /// </summary>
    internal unsafe class Decoder : Wrapper<AVCodecContext>
    {
        private readonly int _bufferLimit;
        private int _bufferSize;
        private bool _reuseLastPacket;
        private MediaPacket? _packet;

        /// <summary>
        /// Initializes a new instance of the <see cref="Decoder"/> class.
        /// </summary>
        /// <param name="codec">The underlying codec.</param>
        /// <param name="stream">The multimedia stream.</param>
        /// <param name="owner">The container that owns the stream.</param>
        public Decoder(AVCodecContext* codec, AVStream* stream, InputContainer owner) : base(codec)
        {
            // convert megabytes to bytes
            _bufferLimit = owner.MaxBufferSize * 1024 * 1024;
            OwnerFile = owner;
            Info = StreamInfo.Create(stream, owner);

            RecentlyDecodedFrame = Info.Type switch
            {
                MediaType.Audio => new AudioFrame(),
                MediaType.Video => new VideoFrame(),
                _ => throw new Exception("Tried to create a decoder from an unsupported stream or codec type."),
            };

            BufferedPackets = new Queue<MediaPacket>();
        }

        /// <summary>
        /// Gets informations about the stream.
        /// </summary>
        public StreamInfo Info { get; }

        /// <summary>
        /// Gets the media container that owns this stream.
        /// </summary>
        public InputContainer OwnerFile { get; }

        /// <summary>
        /// Gets the recently decoded frame.
        /// </summary>
        public MediaFrame RecentlyDecodedFrame { get; }

        /// <summary>
        /// Indicates whether the codec has buffered packets.
        /// </summary>
        public bool IsBufferEmpty => BufferedPackets.Count == 0;

        /// <summary>
        /// Gets a FIFO collection of media packets that the codec has buffered.
        /// </summary>
        private Queue<MediaPacket> BufferedPackets { get; }

        /// <summary>
        /// Adds the specified packet to the codec buffer.
        /// </summary>
        /// <param name="packet">The packet to be buffered.</param>
        public void BufferPacket(MediaPacket packet)
        {
            BufferedPackets.Enqueue(packet);
            _bufferSize += packet.Pointer->size;

            if (_bufferSize > _bufferLimit)
            {
                var deletedPacket = BufferedPackets.Dequeue();
                _bufferSize -= deletedPacket.Pointer->size;
                deletedPacket.Dispose();
            }
        }

        /// <summary>
        /// Reads the next frame from the stream.
        /// </summary>
        /// <returns>The decoded frame.</returns>
        public MediaFrame GetNextFrame()
        {
            ReadNextFrame();
            return RecentlyDecodedFrame;
        }

        /// <summary>
        /// Decodes frames until reach the specified time stamp. Useful to seek few frames forward.
        /// </summary>
        /// <param name="targetTs">The target time stamp.</param>
        public void SkipFrames(long targetTs)
        {
            do
            {
                ReadNextFrame();
            }
            while (RecentlyDecodedFrame.PresentationTimestamp < targetTs);
        }

        /// <summary>
        /// Discards all packet data buffered by this instance.
        /// </summary>
        public void DiscardBufferedData()
        {
            foreach (var packet in BufferedPackets)
            {
                packet.Wipe();
                packet.Dispose();
            }

            BufferedPackets.Clear();
            _bufferSize = 0;
        }

        /// <summary>
        /// Flushes the codec buffers.
        /// </summary>
        public void FlushUnmanagedBuffers()
        {
            ffmpeg.avcodec_flush_buffers(Pointer);
        }

        /// <inheritdoc/>
        protected override void OnDisposing()
        {
            RecentlyDecodedFrame.Dispose();
            FlushUnmanagedBuffers();
            ffmpeg.avcodec_close(Pointer);
        }

        private void ReadNextFrame()
        {
            ffmpeg.av_frame_unref(RecentlyDecodedFrame.Pointer);
            int error;

            do
            {
                // Gets the next packet and sends it to the decoder
                DecodePacket();

                // Tries to decode frame from the packets.
                error = ffmpeg.avcodec_receive_frame(Pointer, RecentlyDecodedFrame.Pointer);
            }
            while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == -35); // The EAGAIN code means that the frame decoding has not been completed and more packets are needed.
            error.ThrowIfError("An error occurred while decoding the frame.");
        }

        private void DecodePacket()
        {
            if (!_reuseLastPacket)
            {
                if (IsBufferEmpty)
                {
                    OwnerFile.GetPacketFromStream(Info.Index);
                }

                _packet = BufferedPackets.Dequeue();
                _bufferSize -= _packet.Pointer->size;
            }

            // Sends the packet to the decoder.
            var result = ffmpeg.avcodec_send_packet(Pointer, _packet!);

            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                _reuseLastPacket = true;
            }
            else
            {
                _reuseLastPacket = false;
                result.ThrowIfError("Cannot send a packet to the decoder.");
                _packet?.Wipe();
            }
        }
    }
}
