using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Extensions.FFmpeg.Encoding;
using BEditor.Media;
using BEditor.Media.Encoding;

namespace BEditor.Extensions.FFmpeg
{
    public class RegisterdEncoding : IRegisterdEncoding
    {
        public string Name => "FFmpeg";

        public IOutputContainer? Create(string file)
        {
            return new OutputContainer(file);
        }

        public IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
            yield return ".avi";
            yield return ".wmv";
            yield return ".mov";
            yield return ".webm";
            yield return ".ogv";
            yield return ".mkv";
            yield return ".flv";
            yield return ".3gp";
            yield return ".3g2";
        }
    }
}