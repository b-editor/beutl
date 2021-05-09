using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media
{
    /// <summary>
    /// Represents the audio.
    /// </summary>
    /// <typeparam name="T">The type of audio data.</typeparam>
    public unsafe partial class Sound<T> : IDisposable, ICloneable where T : unmanaged, IPCM<T>
    {
        private readonly bool _requireDispose = true;
        private T* _pointer;
        private T[]? _array;

        /// <summary>
        /// Initializes a new instance of the <see cref="Sound{T}"/> class.
        /// </summary>
        /// <param name="rate">The sample rate.</param>
        /// <param name="length">The length of data.</param>
        public Sound(int rate, int length)
        {
            SampleRate = rate;
            Length = length;

            _pointer = (T*)Marshal.AllocCoTaskMem(DataSize);
            Data.Fill(default);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sound{T}"/> class with a specified data.
        /// </summary>
        /// <param name="rate">The sample rate.</param>
        /// <param name="length">The length of data.</param>
        /// <param name="data">The audio data.</param>
        public Sound(int rate, int length, T[] data)
        {
            _requireDispose = false;
            SampleRate = rate;
            Length = length;

            _array = data;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sound{T}"/> class with a specified data.
        /// </summary>
        /// <param name="rate">The sample rate.</param>
        /// <param name="length">The length of data.</param>
        /// <param name="data">The audio data.</param>
        public Sound(int rate, int length, T* data)
        {
            _requireDispose = false;
            SampleRate = rate;
            Length = length;

            _pointer = data;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sound{T}"/> class with a specified data.
        /// </summary>
        /// <param name="rate">The sample rate.</param>
        /// <param name="length">The length of data.</param>
        /// <param name="data">The audio data.</param>
        public Sound(int rate, int length, IntPtr data)
        {
            _requireDispose = false;
            SampleRate = rate;
            Length = length;

            _pointer = (T*)data;
        }

        /// <summary>
        /// Discards the reference to the target that is represented by the current <see cref="Sound{T}"/> object.
        /// </summary>
        ~Sound()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the audio data.
        /// </summary>
        public Span<T> Data
        {
            get
            {
                ThrowIfDisposed();

                return (_array is null) ? new Span<T>(_pointer, Length) : new Span<T>(_array);
            }
        }

        /// <summary>
        /// Gets the sample rate of <see cref="Sound{T}"/>.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets the length of data.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the length of this sound.
        /// </summary>
        public TimeSpan Time => TimeSpan.FromSeconds(Length / (double)SampleRate);

        /// <summary>
        /// Get the data size of <see cref="Sound{T}"/>.
        /// </summary>
        public int DataSize => Length * sizeof(T);

        /// <summary>
        /// Get whether an object has been disposed
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Converts the data in this <see cref="Sound{T}"/> to the specified type.
        /// </summary>
        /// <typeparam name="TConvert">The type of audio data to convert to.</typeparam>
        public Sound<TConvert> Convert<TConvert>() where TConvert : unmanaged, IPCM<TConvert>, IPCMConvertable<T>
        {
            var result = new Sound<TConvert>(SampleRate, Length);

            Parallel.For(0, Length, i => result.Data[i].ConvertFrom(Data[i]));

            return result;
        }

        /// <summary>
        /// If this object has already been discarded, throw an exception.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(Sound<T>));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed && _requireDispose)
            {
                if (_pointer != null) Marshal.FreeCoTaskMem((IntPtr)_pointer);

                _pointer = null;
                _array = null;
            }
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc cref="ICloneable.Clone"/>
        public Sound<T> Clone()
        {
            ThrowIfDisposed();

            var img = new Sound<T>(SampleRate, Length);
            Data.CopyTo(img.Data);

            return img;
        }

        /// <summary>
        /// Forms a slice of the current <see cref="Sound{T}"/>, starting at the specified time.
        /// </summary>
        /// <param name="start">The time at which to begin the slice.</param>
        public Sound<T> Slice(TimeSpan start)
        {
            var data = Data[(int)(start.TotalSeconds * SampleRate)..];

            fixed (T* dataPtr = data)
            {
                return new(SampleRate, data.Length, dataPtr);
            }
        }

        /// <summary>
        /// Forms a slice of the specified length from the current <see cref="Sound{T}"/> starting at the specified time.
        /// </summary>
        /// <param name="start">The time at which to begin this slice.</param>
        /// <param name="length">The desired length for the slice.</param>
        public Sound<T> Slice(TimeSpan start, TimeSpan length)
        {
            var data = Data.Slice((int)(start.TotalSeconds * SampleRate), (int)(length.TotalSeconds * SampleRate));

            fixed (T* dataPtr = data)
            {
                return new(SampleRate, data.Length, dataPtr);
            }
        }

        /// <summary>
        /// Add the specified <see cref="Sound{T}"/> to this <see cref="Sound{T}"/>.
        /// </summary>
        /// <param name="sound">The sound to add.</param>
        public void Add(Sound<T> sound)
        {
            // Todo : Add message
            if (sound.SampleRate != SampleRate) throw new Exception();

            Parallel.For(0, Math.Min(sound.Length, Length), i => Data[i] = Data[i].Add(sound.Data[i]));
        }

        /// <summary>
        /// Resamples the <see cref="Sound{T}"/>.
        /// </summary>
        /// <param name="frequency">The new sampling frequency.</param>
        public Sound<T> Resamples(int frequency)
        {
            if (SampleRate == frequency) return Clone();

            // 比率
            var ratio = SampleRate / (float)frequency;
            // 1チャンネルのサイズ
            var size = frequency * Time.TotalSeconds;

            using var tmp = new UnmanagedArray<T>((int)Math.Floor((double)size));
            var index = 0f;
            for (var i = 0; i < tmp.Length; i++)
            {
                index += ratio;
                tmp[i] = Data[(int)Math.Floor(index)];
            }

            var result = new Sound<T>(frequency, tmp.Length);
            tmp.AsSpan().CopyTo(result.Data);

            return result;
        }

        /// <inheritdoc/>
        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}