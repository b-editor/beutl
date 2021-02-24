using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media
{
    public unsafe class Sound<T> where T : unmanaged, IPCM<T>
    {
        public Sound(uint rate, uint length)
        {
            Samplingrate = rate;
            Length = length;
            Pcm = new T[length];
        }

        public T[] Pcm { get; }
        public uint Samplingrate { get; }
        public uint Length { get; }
        public long DataSize => (uint)Length * sizeof(T);

        public Sound<TConvert> Convert<TConvert>() where TConvert : unmanaged, IPCM<TConvert>, IPCMConvertable<T>
        {
            var result = new Sound<TConvert>(Samplingrate, Length);

            Parallel.For(0, Length, i =>
            {
                result.Pcm[i].ConvertFrom(Pcm[i]);
            });

            return result;
        }
    }
}
