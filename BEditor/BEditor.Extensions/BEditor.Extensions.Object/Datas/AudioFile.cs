using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.AudioFile)]
    public class AudioFile : IExEffect
    {
        public AudioFile(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.AudioFile)!;
            var pos = e.Values[Consts.PlayPosition];
            var speed = e.Values[Consts.PlaySpeed];
            var isLoop = e.Values[Consts.LoopPlay];
            var link = e.Values[Consts.LinkVideoFile];
            File = e.Values[Consts.file];

            Position = float.Parse(pos);
            Speed = float.Parse(speed);
            Loop = int.Parse(isLoop) != 0;
            Link = int.Parse(link) != 0;
        }

        public float Position { get; }
        public float Speed { get; }
        public bool Loop { get; }
        public bool Link { get; }
        public string File { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }

        /*
_name=音声ファイル
再生位置=0.00
再生速度=100.0
ループ再生=0
動画ファイルと連携=0
file=
         */
    }
}
