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
        public Sound(Channel channels, uint rate, uint length)
        {
            Channels = channels;
            Samplingrate = rate;
            Length = length;
            Pcm = new T[(uint)channels * length];
        }

        public T[] Pcm { get; }
        public Channel Channels { get; }
        public uint Samplingrate { get; }
        public uint Length { get; }
        public long DataSize => (uint)Channels  * Length * sizeof(T);
    }

    public enum Channel : uint
    {
        Monaural = 1,
        Stereo = 2
    }
}
