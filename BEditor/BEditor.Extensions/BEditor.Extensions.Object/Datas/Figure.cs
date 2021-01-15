using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.Figure)]
    public class Figure : IExEffect
    {
        public enum Type
        {
            Circle = 2
        }

        public Figure(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.Figure)!;
            Size = int.Parse(e.Values[Consts.Size]);
            AspectRatio = int.Parse(e.Values[Consts.AspectRatio]);
            LineWidth = int.Parse(e.Values[Consts.LineWidth]);
            FigureType = (Type)int.Parse(e.Values[Consts.type]);
            Color = uint.Parse(e.Values[Consts.color]);
            Name = e.Values[Consts.name];
        }

        public int Size { get; }
        public int AspectRatio { get; }
        public int LineWidth { get; }
        public Type FigureType { get; }
        public uint Color { get; }
        public string Name { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }
        /*
_name=図形
サイズ=100
縦横比=0.00
ライン幅=4000
type=2
color=FFFFFF
name=
         */
    }
}
