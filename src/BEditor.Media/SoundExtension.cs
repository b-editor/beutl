using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.PCM;

namespace BEditor.Media
{
    public unsafe static class Sound
    {
        public static Sound<PCM16> SinWave(uint samplingrate)
        {
            var sound = new Sound<PCM16>(samplingrate, samplingrate * 1);

            fixed (PCM16* pcm = sound.Pcm)
            {
                for (int i = 0; i < samplingrate; i++)
                {
                    pcm[i].Value = (short)(32767 * Math.Sin(2 * Math.PI * i * 440 / samplingrate));
                }
            }

            return sound;
        }

        public static Sound<TConvert> Convert<TConvert, TSource>(this Sound<TSource> self) where TSource : unmanaged, IPCM<TSource>, IPCMConvertable<TConvert> where TConvert : unmanaged, IPCM<TConvert>
        {
            var result = new Sound<TConvert>(self.Samplingrate, self.Length);

            Parallel.For(0, self.Length, i =>
            {
                self.Pcm[i].ConvertTo(out result.Pcm[i]);
            });

            return result;
        }
    }
}
