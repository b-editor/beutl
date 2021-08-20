using System;

using Vortice.Multimedia;

namespace BEditor.Audio.XAudio2
{
    public sealed class XAudioBuffer : IDisposable
    {
        private UnmanagedArray<byte>?_stream;

        public XAudioBuffer()
        {
            Buffer = new Vortice.XAudio2.AudioBuffer();
        }

        public Vortice.XAudio2.AudioBuffer Buffer { get; }

        public int SizeInBytes { get; private set; }
        
        public WaveFormat? Format { get; private set; }

        public unsafe void BufferData<T>(Span<T> buffer, WaveFormat format)
            where T : unmanaged
        {
            int sizeInBytes = sizeof(T) * buffer.Length;

            fixed (T* ptr = buffer)
            {
                BufferData((IntPtr)ptr, sizeInBytes, format);
            }
        }

        public unsafe void BufferData(IntPtr buffer, int sizeInBytes, WaveFormat format)
        {
            _stream?.Dispose();
            _stream = new(sizeInBytes);
            System.Buffer.MemoryCopy((void*)buffer, (void*)_stream.Pointer, sizeInBytes, sizeInBytes);

            Format = format;
            SizeInBytes = sizeInBytes;
            Buffer.AudioDataPointer = _stream.Pointer;
            Buffer.AudioBytes = SizeInBytes;
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }
}
