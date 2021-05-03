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
        public static Sound<TConvert> Convert<TConvert, TSource>(this Sound<TSource> self)
            where TConvert : unmanaged, IPCM<TConvert>
            where TSource : unmanaged, IPCM<TSource>, IPCMConvertable<TConvert>
        {
            var result = new Sound<TConvert>(self.SampleRate, self.Length);

            Parallel.For(0, self.Length, i =>
            {
                self.Data[i].ConvertTo(out result.Data[i]);
            });

            return result;
        }

        public static void SetChannelData(this Sound<StereoPCMFloat> sound, int channel, Span<float> data)
        {
            fixed (StereoPCMFloat* dst = sound.Data)
            {
                var dataf = (float*)dst;
                var soundlength = sound.Length * 2;
                var length = data.Length;
                var l = 0;

                for (var i = channel; l < length && i < soundlength; i += 2, l++)
                {
                    dataf[i] = data[l];
                }
            }
        }

        public static void SetChannelData(this Sound<StereoPCMFloat> sound, int start, int channel, Span<float> data)
        {
#pragma warning disable RCS1176
            fixed (StereoPCMFloat* dst = &sound.Data[start])
#pragma warning restore RCS1176
            {
                var dataf = (float*)dst;
                var soundlength = (sound.Length - start) * 2;
                var length = data.Length;
                var l = 0;

                for (var i = channel; l < length && i < soundlength; i += 2, l++)
                {
                    dataf[i] = data[l];
                }
            }
        }

        public static float[][] Extract(this Sound<StereoPCMFloat> sound)
        {
            var left = new float[sound.Data.Length];
            var right = new float[sound.Data.Length];

            for (var i = 0; i < sound.Data.Length; i++)
            {
                left[i] = sound.Data[i].Left;
                right[i] = sound.Data[i].Right;
            }

            return new float[][] { left, right };
        }
    }
}