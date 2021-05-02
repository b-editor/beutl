using System;
using System.IO;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

#pragma warning disable IDE0060, RCS1163

namespace BEditor.Media.Decoding.Internal
{
    /// <summary>
    /// A stream wrapper.
    /// </summary>
    internal unsafe class AvioStream
    {
        private readonly Stream _inputStream;
        private byte[]? _readBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvioStream"/> class.
        /// </summary>
        /// <param name="input">Multimedia file stream.</param>
        public AvioStream(Stream input)
        {
            _inputStream = input ?? throw new ArgumentNullException(nameof(input));
        }

        /// <summary>
        /// A method for refilling the buffer. For stream protocols,
        /// must never return 0 but rather a proper AVERROR code.
        /// </summary>
        /// <param name="opaque">An opaque pointer.</param>
        /// <param name="buffer">A buffer that needs to be filled with stream data.</param>
        /// <param name="bufferLength">The size of <paramref name="buffer"/>.</param>
        /// <returns>Number of read bytes.</returns>
        public int Read(void* opaque, byte* buffer, int bufferLength)
        {
            _readBuffer ??= new byte[bufferLength];

            var readed = _inputStream.Read(_readBuffer, 0, _readBuffer.Length);
            if (readed > 0)
            {
                Marshal.Copy(_readBuffer, 0, (IntPtr)buffer, readed);
            }

            return readed;
        }

        /// <summary>
        /// A method for seeking to specified byte position.
        /// </summary>
        /// <param name="opaque">An opaque pointer.</param>
        /// <param name="offset">The offset in a stream.</param>
        /// <param name="whence">The seek option.</param>
        /// <returns>Position within the current stream or stream size.</returns>
        public long Seek(void* opaque, long offset, int whence)
        {
            if (!_inputStream.CanSeek)
            {
                return -1;
            }

            return whence == ffmpeg.AVSEEK_SIZE ?
                _inputStream.Length :
                _inputStream.Seek(offset, SeekOrigin.Begin);
        }
    }
}
#pragma warning restore RCS1163, IDE0060