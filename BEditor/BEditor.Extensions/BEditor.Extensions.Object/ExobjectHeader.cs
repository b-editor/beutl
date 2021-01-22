using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BEditor.Extensions.Object
{
    public class ExobjectHeader
    {
        public ExobjectHeader(Dictionary<string, string> pairs)
        {
            Start = int.Parse(pairs["start"]);
            End = int.Parse(pairs["end"]);
            Layer = int.Parse(pairs["layer"]);
            Group = int.Parse(pairs["group"]);
            Overlay = int.Parse(pairs["overlay"]);
            Camera = int.Parse(pairs["camera"]);
            Audio = int.Parse(pairs["audio"]);
        }

        public int Start { get; }
        public int End { get; }
        public int Layer { get; }
        public int Group { get; }
        public int Overlay { get; }
        public int Camera { get; }
        public int Audio { get; }

        public override string ToString()
        {
            return
                $"start={Start}{ExoPerser.returnStr}" +
                $"end={End}{ExoPerser.returnStr}" +
                $"layer={Layer}{ExoPerser.returnStr}" +
                $"group={Group}{ExoPerser.returnStr}" +
                $"overlay={Overlay}{ExoPerser.returnStr}" +
                $"camera={Camera}{ExoPerser.returnStr}" +
                $"audio={Audio}{ExoPerser.returnStr}";
        }
        /*
[0]
start=1
end=300
layer=1
group=0
overlay=1 
camera=0 カメラオブジェクトかのフラグ
audio=0 音声オブジェクトかのフラグ
         */
    }
}
