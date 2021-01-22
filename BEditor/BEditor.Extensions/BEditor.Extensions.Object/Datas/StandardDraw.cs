using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.StandardDraw)]
    public class StandardDraw : IExEffect
    {
        public StandardDraw(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.StandardDraw)!;
            X = float.Parse(e.Values[Consts.X]);
            Y = float.Parse(e.Values[Consts.Y]);
            Z = float.Parse(e.Values[Consts.Z]);
            Scale = float.Parse(e.Values[Consts.Scale]);
            Opacity = float.Parse(e.Values[Consts.Opacity]);
            Rotate = float.Parse(e.Values[Consts.Rotate]);
            Blend = int.Parse(e.Values[Consts.blend]);
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Scale { get; }
        public float Opacity { get; }
        public float Rotate { get; }
        public int Blend { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }
        /*
_name=標準描画
X=0.0
Y=0.0
Z=0.0
拡大率=100.00
透明度=0.0
回転=0.00
blend=0
*/
    }
}
