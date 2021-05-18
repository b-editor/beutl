using System;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// A base for streams of any kind of media.
    /// </summary>
    public interface IMediaStream : IDisposable
    {
        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public StreamInfo Info { get; }
    }
}