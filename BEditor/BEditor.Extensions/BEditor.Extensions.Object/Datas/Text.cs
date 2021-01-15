using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    [Named(Consts.Text)]
    public class Text : IExEffect
    {
        public Text(Exobject exobject)
        {
            var e = exobject.RawEffects.Find(i => i.Name == Consts.Text)!;

            Size = int.Parse(e.Values[Consts.Size]);
            DisplaySpeed = int.Parse(e.Values[Consts.DisplaySpeed]);
            IndividualObject = int.Parse(e.Values[Consts.IndividualObject]) != 0;
            AutoScroll = int.Parse(e.Values[Consts.AutoScroll]) != 0;
            Bold = int.Parse(e.Values[Consts.Bold]) != 0;
            Italic = int.Parse(e.Values[Consts.Italic]) != 0;
            Type = int.Parse(e.Values[Consts.type]);
            AutoAdjust = int.Parse(e.Values[Consts.autoadjust]);
            Soft = int.Parse(e.Values[Consts.soft]);
            MonoSpace = int.Parse(e.Values[Consts.monospace]);
            Align = int.Parse(e.Values[Consts.align]);
            SpacingX = int.Parse(e.Values[Consts.spacing_x]);
            SpacingY = int.Parse(e.Values[Consts.spacing_y]);
            Precision = int.Parse(e.Values[Consts.precision]);
            Color = uint.Parse(e.Values[Consts.color]);
            Color2 = uint.Parse(e.Values[Consts.color2]);
            Font = e.Values[Consts.font];
            Text_ = e.Values[Consts.text];
        }

        public int Size { get; }
        public float DisplaySpeed { get; }
        public bool IndividualObject { get; }
        public bool DisplayOnMovingCoord { get; }
        public bool AutoScroll { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public int Type { get; }
        public int AutoAdjust { get; }
        public int Soft { get; }
        public int MonoSpace { get; }
        public int Align { get; }
        public int SpacingX { get; }
        public int SpacingY { get; }
        public int Precision { get; }
        public uint Color { get; }
        public uint Color2 { get; }
        public string Font { get; }
        public string Text_ { get; }

        public EffectElement ToEffectElement(Exobject exobject)
        {
            throw new NotImplementedException();
        }

        /*
_name=テキスト
サイズ=34
表示速度=0.0
文字毎に個別オブジェクト=0
移動座標上に表示する=0
自動スクロール=0
B=0
I=0
type=0
autoadjust=0
soft=1
monospace=0
align=0
spacing_x=0
spacing_y=25
precision=1
color=FFFFFF
color2=000000
font=メイリオ
text=
         */
    }
}
