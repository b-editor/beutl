using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Audio.Platform;

namespace BEditor.Audio.XAudio2
{
    public sealed class AudioBufferImpl : IAudioBufferImpl
    {
        internal Vortice.XAudio2.AudioBuffer? Buffer;

        public AudioBufferImpl()
        {
        }

        public int TotalSamples => SizeInBytes / Format.BytesPerSample;

        public int SizeInBytes { get; private set; }

        public AudioFormat Format { get; private set; }

        public unsafe void BufferData<T>(Span<T> buffer, AudioFormat format)
            where T : unmanaged
        {
            int sizeInBytes = sizeof(T) * buffer.Length;

            fixed (T* ptr = buffer)
            {
                Format = format;
                SizeInBytes = sizeInBytes;
                Buffer?.Dispose();
                Buffer = Vortice.XAudio2.AudioBuffer.Create<T>(buffer, Vortice.XAudio2.BufferFlags.EndOfStream);
            }
        }

        public void Dispose()
        {
            Buffer?.Dispose();
        }
    }
}
