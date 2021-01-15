using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BEditor.Extensions.Object
{
    public class ExeditHeader
    {
        public ExeditHeader(Dictionary<string, string> pairs)
        {
            Width = int.Parse(pairs["width"]);
            Height = int.Parse(pairs["height"]);
            Rate = int.Parse(pairs["rate"]);
            Scale = int.Parse(pairs["scale"]);
            AudioRate = int.Parse(pairs["audio_rate"]);
            AudioCh = int.Parse(pairs["audio_ch"]);
        }

        public int Width { get; }
        public int Height { get; }
        public int Rate { get; }
        public int Scale { get; }
        public int AudioRate { get; }
        public int AudioCh { get; }

        public override string ToString()
        {
            return
                $"width={Width}{ExoPerser.returnStr}" +
                $"height={Height}{ExoPerser.returnStr}" +
                $"rate={Rate}{ExoPerser.returnStr}" +
                $"scale={Scale}{ExoPerser.returnStr}" +
                $"audio_rate={AudioRate}{ExoPerser.returnStr}" +
                $"audio_ch={AudioCh}{ExoPerser.returnStr}";
        }

        /*
         * [exedit]
         * width=1920
         * height=1080
         * rate=30
         * scale=1
         * audio_rate=48000
         * audio_ch=2
         */
    }
}
