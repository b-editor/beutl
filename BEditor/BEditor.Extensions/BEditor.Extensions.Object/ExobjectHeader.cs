using System;
using System.Text.RegularExpressions;

namespace BEditor.Extensions.Object
{
    public class ExobjectHeader
    {
        private readonly static Regex indexRegex = new Regex(@"^\[([\d]+)\]\z");
        private readonly static Regex startRegex = new Regex(@"^start=([\d]+)\z");
        private readonly static Regex endRegex = new Regex(@"^end=([\d]+)\z");
        private readonly static Regex layerRegex = new Regex(@"^layer=([\d]+)\z");
        private readonly static Regex groupRegex = new Regex(@"^group=([\d]+)\z");
        private readonly static Regex overlayRegex = new Regex(@"^overlay=([\d]+)\z");
        private readonly static Regex cameraRegex = new Regex(@"^camera=([\d]+)\z");
        private readonly static Regex audioRegex = new Regex(@"^audio=([\d]+)\z");

        public ExobjectHeader(ReadOnlySpan<string> lines)
        {
            if (lines.Length is not 8) throw new FormatException();

            Index = indexRegex.Match(lines[0]).Groups[1].Value;

            foreach (var line in lines[1..])
            {
                if (startRegex.IsMatch(line)) Start = startRegex.Match(line).Groups[1].Value;
                else if (endRegex.IsMatch(line)) End = endRegex.Match(line).Groups[1].Value;
                else if (layerRegex.IsMatch(line)) Layer = layerRegex.Match(line).Groups[1].Value;
                else if (groupRegex.IsMatch(line)) Group = groupRegex.Match(line).Groups[1].Value;
                else if (overlayRegex.IsMatch(line)) Overlay = overlayRegex.Match(line).Groups[1].Value;
                else if (cameraRegex.IsMatch(line)) Camera = cameraRegex.Match(line).Groups[1].Value;
                else if (audioRegex.IsMatch(line)) Audio = audioRegex.Match(line).Groups[1].Value;
            }
        }

        public string Index { get; }
        public string Start { get; }
        public string End { get; }
        public string Layer { get; }
        public string Group { get; }
        public string Overlay { get; }
        public string Camera { get; }
        public string Audio { get; }
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
