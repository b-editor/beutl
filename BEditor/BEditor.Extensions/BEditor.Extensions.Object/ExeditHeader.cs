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
        private readonly static Regex widthRegex = new Regex(@"^width=([\d]+)\z");
        private readonly static Regex heightRegex = new Regex(@"^height=([\d]+)\z");
        private readonly static Regex rateRegex = new Regex(@"^rate=([\d]+)\z");
        private readonly static Regex scaleRegex = new Regex(@"^scale=([\d]+)\z");
        private readonly static Regex audio_rateRegex = new Regex(@"^audio_rate=([\d]+)\z");
        private readonly static Regex audio_chRegex = new Regex(@"^audio_ch=([\d]+)\z");

        public ExeditHeader(ReadOnlySpan<string> lines)
        {
            if (lines.Length is not 7) throw new FormatException();

            foreach (var line in lines[1..])
            {
                if (widthRegex.IsMatch(line)) Width = widthRegex.Match(line).Groups[1].Value;
                else if (heightRegex.IsMatch(line)) Height = heightRegex.Match(line).Groups[1].Value;
                else if (rateRegex.IsMatch(line)) Rate = rateRegex.Match(line).Groups[1].Value;
                else if (scaleRegex.IsMatch(line)) Scale = scaleRegex.Match(line).Groups[1].Value;
                else if (audio_rateRegex.IsMatch(line)) AudioRate = audio_rateRegex.Match(line).Groups[1].Value;
                else if (audio_chRegex.IsMatch(line)) AudioCh = audio_chRegex.Match(line).Groups[1].Value;
            }
        }

        public string Width { get; }
        public string Height { get; }
        public string Rate { get; }
        public string Scale { get; }
        public string AudioRate { get; }
        public string AudioCh { get; }

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
