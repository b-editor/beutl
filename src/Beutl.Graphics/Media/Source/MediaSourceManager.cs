using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

public class MediaSourceManager
{
    private readonly Dictionary<string, Ref<MediaReader>> _mediaReaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Ref<Bitmap<Bgra8888>>> _bitmaps = new(StringComparer.Ordinal);

    public bool OpenVideoSource(string name, [NotNullWhen(true)] out IVideoSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new VideoSource(mediaReader, name);
        }

        return value != null;
    }

    public bool OpenSoundSource(string name, [NotNullWhen(true)] out ISoundSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new SoundSource(mediaReader, name);
        }

        return value != null;
    }

    public bool OpenImageSource(string name, [NotNullWhen(true)] out IImageSource? value)
    {
        value = null;
        if (TryGetBitmapOrOpen(name, out Ref<Bitmap<Bgra8888>>? bitmap))
        {
            value = new ImageSource(bitmap, name);
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetMediaReader(string name, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        value = null;
        if (_mediaReaders.TryGetValue(name, out Ref<MediaReader>? result))
        {
            value = result.Clone();
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetMediaReaderOrOpen(string fileName, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        value = null;
        if (_mediaReaders.TryGetValue(fileName, out Ref<MediaReader>? result))
        {
            value = result.Clone();
        }
        else
        {
            try
            {
                var reader = MediaReader.Open(fileName);

                var counter = new Counter<MediaReader>(reader, () => _mediaReaders.Remove(fileName));
                value = new Ref<MediaReader>(reader, counter);
                _mediaReaders.Add(fileName, value);
            }
            catch
            {
            }
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetBitmap(string name, [NotNullWhen(true)] out Ref<Bitmap<Bgra8888>>? value)
    {
        value = null;
        if (_bitmaps.TryGetValue(name, out var result))
        {
            value = result.Clone();
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetBitmapOrOpen(string fileName, [NotNullWhen(true)] out Ref<Bitmap<Bgra8888>>? value)
    {
        value = null;
        if (_bitmaps.TryGetValue(fileName, out Ref<Bitmap<Bgra8888>>? result))
        {
            value = result.Clone();
        }
        else
        {
            try
            {
                var bitmap = Bitmap<Bgra8888>.FromFile(fileName);

                var counter = new Counter<Bitmap<Bgra8888>>(bitmap, () => _bitmaps.Remove(fileName));
                value = new Ref<Bitmap<Bgra8888>>(bitmap, counter);
                _bitmaps.Add(fileName, value);
            }
            catch
            {
            }
        }

        return value != null;
    }

    // 移譲する側は今後、valueを直接参照しない。
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryTransferMediaReader(string name, MediaReader mediaReader)
    {
        if (mediaReader.IsDisposed
            || _mediaReaders.ContainsKey(name)
            || _mediaReaders.Values.Any(x => ReferenceEquals(x.Value, mediaReader)))
        {
            return false;
        }
        else
        {
            var counter = new Counter<MediaReader>(mediaReader, () => _mediaReaders.Remove(name));
            var @ref = new Ref<MediaReader>(mediaReader, counter);
            _mediaReaders.Add(name, @ref);
            return true;
        }
    }

    // 移譲する側は今後、valueを直接参照しない。
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryTransferBitmap(string name, Bitmap<Bgra8888> bitmap)
    {
        if (bitmap.IsDisposed
            || _bitmaps.ContainsKey(name)
            || _bitmaps.Values.Any(x => ReferenceEquals(x.Value, bitmap)))
        {
            return false;
        }
        else
        {
            var counter = new Counter<Bitmap<Bgra8888>>(bitmap, () => _bitmaps.Remove(name));
            var @ref = new Ref<Bitmap<Bgra8888>>(bitmap, counter);
            _bitmaps.Add(name, @ref);
            return true;
        }
    }

    public bool ContainsMediaReader(string name)
    {
        return _mediaReaders.ContainsKey(name);
    }

    public bool ContainsBitmap(string name)
    {
        return _bitmaps.ContainsKey(name);
    }

    internal sealed class Counter<T>
        where T : class, IDisposable
    {
        private T? _value;
        private Action? _onRelease;
        private int _refs;

        public Counter(T value, Action onRelease)
        {
            _value = value;
            _onRelease = onRelease;
        }

        public void AddRef()
        {
            var old = _refs;
            while (true)
            {
                if (old == 0)
                {
                    throw new ObjectDisposedException("Cannot add a reference to a nonreferenced item");
                }
                var current = Interlocked.CompareExchange(ref _refs, old + 1, old);
                if (current == old)
                {
                    break;
                }
                old = current;
            }
        }

        public void Release()
        {
            var old = _refs;
            while (true)
            {
                var current = Interlocked.CompareExchange(ref _refs, old - 1, old);

                if (current == old)
                {
                    if (old == 1)
                    {
                        _onRelease?.Invoke();
                        _onRelease = null;

                        _value?.Dispose();
                        _value = null;
                    }
                    break;
                }
                old = current;
            }
        }

        public int RefCount => _refs;
    }

    public sealed class Ref<T> : IDisposable
        where T : class, IDisposable
    {
        private readonly Counter<T> _counter;
        private readonly object _lock = new();

        internal Ref(T value, Counter<T> counter)
        {
            Value = value;
            _counter = counter;
        }

        ~Ref()
        {
            Dispose();
        }

        public T Value { get; private set; }

        public int RefCount => _counter.RefCount;

        public Ref<T> Clone()
        {
            lock (_lock)
            {
                if (Value != null)
                {
                    var newRef = new Ref<T>(Value, _counter);
                    _counter.AddRef();
                    return newRef;
                }
                throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (Value != null)
                {
                    _counter.Release();
                    Value = null!;
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
