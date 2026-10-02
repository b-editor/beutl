using System.Runtime.InteropServices;

using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Beutl.Audio.Platforms.XAudio2;

public sealed unsafe class XAudioBuffer : IDisposable
{
    private void* _stream;
    private bool _isDisposed;

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
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_stream != null)
        {
            NativeMemory.Free(_stream);
            _stream = null;
        }

        _stream = NativeMemory.AllocZeroed((nuint)sizeInBytes);
        System.Buffer.MemoryCopy((void*)buffer, _stream, sizeInBytes, sizeInBytes);

        Format = format;
        SizeInBytes = sizeInBytes;
        Buffer.AudioDataPointer = (nint)_stream;
        Buffer.AudioBytes = (uint)SizeInBytes;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Buffer.Dispose();
        if (_stream != null)
        {
            NativeMemory.Free(_stream);
            _stream = null;
        }

        Buffer.AudioDataPointer = IntPtr.Zero;
        Buffer.AudioBytes = 0;
        SizeInBytes = 0;
        Format = null;
    }
}
