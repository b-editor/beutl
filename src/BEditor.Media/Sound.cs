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
        private readonly uint length;

        public Sound(Channel channels, uint rate, uint length)
        {
            Channels = channels;
            Samplingrate = rate;
            this.length = length;
            Pcm = new T[(uint)channels * rate * length];
        }

        public T[] Pcm { get; }
        public Channel Channels { get; }
        public uint Samplingrate { get; }
        public uint Length => (uint)Channels * Samplingrate * length;
        public long DataSize => (uint)Channels * Samplingrate * length * sizeof(T);
    }

    public enum Channel : uint
    {
        Monaural = 1,
        Stereo = 2
    }
}
