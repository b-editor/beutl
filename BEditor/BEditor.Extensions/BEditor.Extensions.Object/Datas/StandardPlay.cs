using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.StandardPlay)]
    public class StandardPlay : IExEffect
    {
        public StandardPlay(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.StandardPlay)!;
            Volume = float.Parse(e.Values[Consts.Volume]);
            LeftRight = float.Parse(e.Values[Consts.LeftRight]);
        }

        public float Volume { get; }
        public float LeftRight { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }

        /*
_name=標準再生
音量=50.0
左右=0.0
         */
    }
}
