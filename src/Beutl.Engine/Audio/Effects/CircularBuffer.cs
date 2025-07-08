using System.Runtime.InteropServices;

namespace Beutl.Audio.Effects;

public sealed unsafe class CircularBuffer<T> : IDisposable
    where T : unmanaged
{
    private readonly T* _buffer;
    private readonly int _length;
    private readonly int _wrapMask;
    private int _writeIndex;
    private bool _disposed;

    public CircularBuffer(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");

        // Round up to next power of 2 for efficient wrapping
        _length = (int)System.Math.Pow(2, System.Math.Ceiling(System.Math.Log(length) / System.Math.Log(2)));
        _wrapMask = _length - 1;
        _writeIndex = 0;

        _buffer = (T*)NativeMemory.AllocZeroed((nuint)(_length * sizeof(T)));
    }

    ~CircularBuffer()
    {
        Dispose(false);
    }

    public int Length => _length;

    public int WriteIndex => _writeIndex;

    public T Read(int samplesBack)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samplesBack < 0)
            throw new ArgumentOutOfRangeException(nameof(samplesBack), "Samples back must be non-negative.");

        if (samplesBack >= _length)
            return default(T); // Return silence for out-of-range reads

        int readIndex = (_writeIndex - samplesBack - 1) & _wrapMask;
        return _buffer[readIndex];
    }

    public void Write(T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _buffer[_writeIndex] = value;
        _writeIndex = (_writeIndex + 1) & _wrapMask;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = 0; i < _length; i++)
        {
            _buffer[i] = default(T);
        }
        _writeIndex = 0;
    }

    public void FillWithValue(T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = 0; i < _length; i++)
        {
            _buffer[i] = value;
        }
    }

    public Span<T> GetInternalBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<T>(_buffer, _length);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            NativeMemory.Free(_buffer);
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
