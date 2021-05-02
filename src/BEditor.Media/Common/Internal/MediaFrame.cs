using FFmpeg.AutoGen;

namespace BEditor.Media.Common.Internal
{
    /// <summary>
    /// Represents a base class of audio and video frames.
    /// </summary>
    internal abstract unsafe class MediaFrame : Wrapper<AVFrame>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MediaFrame"/> class.
        /// </summary>
        /// <param name="frame">The <see cref="AVFrame"/> object.</param>
        protected MediaFrame(AVFrame* frame): base(frame)
        {
        }

        /// <summary>
        /// Gets or sets the frame PTS value in the stream time base units.
        /// </summary>
        public long PresentationTimestamp
        {
            get => Pointer->pts;
            set => Pointer->pts = value;
        }

        /// <summary>
        /// Gets or sets the frame PTS value in the stream time base units.
        /// </summary>
        public long DecodingTimestamp
        {
            get => Pointer->pkt_dts;
            set => Pointer->pkt_dts = value;
        }

        /// <summary>
        /// Changes the pointer to the media frame.
        /// </summary>
        /// <param name="newFrame">The new pointer to a <see cref="AVFrame"/> object.</param>
        internal virtual void Update(AVFrame* newFrame) => UpdatePointer(newFrame);

        /// <inheritdoc/>
        protected override void OnDisposing()
        {
            var ptr = Pointer;
            ffmpeg.av_frame_free(&ptr);
        }
    }
}
