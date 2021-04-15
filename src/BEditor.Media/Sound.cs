using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media
{
    public unsafe class Sound<T> : IDisposable, IAsyncDisposable, ICloneable where T : unmanaged, IPCM<T>
    {
        private readonly bool usedispose = true;
        private T* pointer;
        private T[]? array;

        public Sound(int rate, int length)
        {
            Samplingrate = rate;
            Length = length;

            pointer = (T*)Marshal.AllocHGlobal(DataSize);
            Data.Fill(default);
        }
        public Sound(int rate, int length, T[] data)
        {
            usedispose = false;
            Samplingrate = rate;
            Length = length;

            array = data;
        }
        public Sound(int rate, int length, T* data)
        {
            usedispose = false;
            Samplingrate = rate;
            Length = length;

            pointer = data;
        }
        ~Sound()
        {
            Dispose();
        }

        public Span<T> Data
        {
            get
            {
                ThrowIfDisposed();

                return (array is null) ? new Span<T>(pointer, Length) : new Span<T>(array);
            }
        }
        public int Samplingrate { get; }
        public int Length { get; }
        public int DataSize => (int)(Length * sizeof(T));
        public bool IsDisposed { get; private set; }

        public Sound<TConvert> Convert<TConvert>() where TConvert : unmanaged, IPCM<TConvert>, IPCMConvertable<T>
        {
            var result = new Sound<TConvert>(Samplingrate, Length);

            Parallel.For(0, Length, i =>
            {
                result.Data[i].ConvertFrom(Data[i]);
            });

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(Sound<T>));
        }
        public void Dispose()
        {
            if (!IsDisposed && usedispose)
            {
                if (pointer != null) Marshal.FreeHGlobal((IntPtr)pointer);

                pointer = null;
                array = null;
            }
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
        public ValueTask DisposeAsync()
        {
            if (IsDisposed && !usedispose) return default;

            var task = Task.Run(() =>
            {
                if (pointer != null) Marshal.FreeHGlobal((IntPtr)pointer);

                pointer = null;
                array = null;
            });

            IsDisposed = true;
            GC.SuppressFinalize(this);

            return new(task);
        }
        public Sound<T> Clone()
        {
            ThrowIfDisposed();

            var img = new Sound<T>(Samplingrate, Length);
            Data.CopyTo(img.Data);

            return img;
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}