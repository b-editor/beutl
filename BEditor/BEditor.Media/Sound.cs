using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media
{
    public class Sound
    {
        public Sound(uint channels, uint rate, uint length)
        {
            Channels = channels;
            Samplingrate = rate;
            Pcm = new float[channels * Samplingrate * length];
        }

        public float[] Pcm { get; }
        public uint Channels { get; }
        public uint Samplingrate { get; }
    }
}
