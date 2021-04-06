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
        public static Sound<TConvert> Convert<TConvert, TSource>(this Sound<TSource> self) where TSource : unmanaged, IPCM<TSource>, IPCMConvertable<TConvert> where TConvert : unmanaged, IPCM<TConvert>
        {
            var result = new Sound<TConvert>(self.Samplingrate, self.Length);

            Parallel.For(0, self.Length, i =>
            {
                self.Data[i].ConvertTo(out result.Data[i]);
            });

            return result;
        }
    }
}
