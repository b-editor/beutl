using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object
{
    public class ExoPerser
    {
        const string returnStr = "\r\n";

        private ExoPerser(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public static ExoPerser FromFile(string file)
        {
            var encode = CodePagesEncodingProvider.Instance.GetEncoding("shift-jis");
            // Todo: 最大ファイルサイズが少ないので変更する
            var text = File.ReadAllText(file, encode);

            return new ExoPerser(text);
        }
        public Scene Perse()
        {
            var lines = Text.Split(returnStr).AsSpan();

            var exedit = new ExeditHeader(lines.Slice(0, 7));
            var exos = new List<Exobject>();
            // 行数
            var count = 0;
            lines = lines[7..];

            for (int line = 0; line < lines.Length; line++)
            {
                if (lines[line] == $"[{count}]")
                {
                    count++;
                    var header = new ExobjectHeader(lines.Slice(line, 8));
                    var exo = new Exobject(header);

                    var effect = lines.Slice(line + 8);
                    var effectcount = 0;
                    for (int i = 0; i < effect.Length; i++)
                    {
                        // 次のオブジェクトになったら
                        if (effect[i] == $"[{count}]") break;
                        if (effect[i] == $"[{count - 1}.{effectcount}]")
                        {
                            exo.Effects.Add(new Exeffect(effect.Slice(i)));


                            effectcount++;
                        }
                    }

                    exos.Add(exo);
                }
            }

            return null;
        }
    }
}
