using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.PixelOperation;
using BEditor.Media;
using BEditor.Media.PCM;

namespace BEditor.Audio
{
    public class SamplingContext : IDisposable
    {
        private readonly Sound<StereoPCMFloat> _buffer;

        public SamplingContext(int samplerate, int framerate)
        {
            SamplePerFrame = samplerate / framerate;
            SampleRate = samplerate;

            _buffer = new(samplerate, SamplePerFrame);
        }

        ~SamplingContext()
        {
            Dispose();
        }

        /// <summary>
        /// Get the number of samples per frame.
        /// </summary>
        public int SamplePerFrame { get; }

        /// <summary>
        /// Gets the sample rate.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        public void Clear()
        {
            _buffer.Data.Clear();
        }

        public void Combine(Sound<StereoPCMFloat> sound)
        {
            _buffer.Combine(sound);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                _buffer.Dispose();

                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public void ReadSamples(Sound<StereoPCMFloat> sound)
        {
            var dst = sound.Data;
            var src = _buffer.Data;

            if (src.Length < dst.Length)
            {
                src.Slice(0, dst.Length).CopyTo(dst);
            }
            else
            {
                src.CopyTo(dst.Slice(0, src.Length));
            }
        }

        public Sound<StereoPCMFloat> ReadSamples()
        {
            return _buffer.Clone();
        }
    }
}