using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.VideoFile)]
    public class VideoFile : IExEffect
    {
        public VideoFile(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.VideoFile)!;
            Position = int.Parse(e.Values[Consts.PlayPosition]);
            Speed = int.Parse(e.Values[Consts.PlaySpeed]);
            Loop = int.Parse(e.Values[Consts.LoopPlay]) != 0;
            LoadAlphaCh = int.Parse(e.Values[Consts.LoadAlphaCh]) != 0;
            File = e.Values[Consts.file];
        }

        public int Position { get; }
        public int Speed { get; }
        public bool Loop { get; }
        public bool LoadAlphaCh { get; }
        public string File { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }
        /*
_name=動画ファイル
再生位置=1
再生速度=100.0
ループ再生=0
アルファチャンネルを読み込む=0
file=
*/
    }
}
