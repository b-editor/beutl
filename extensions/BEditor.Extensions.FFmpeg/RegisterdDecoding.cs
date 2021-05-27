using System.Collections.Generic;

using BEditor.Extensions.FFmpeg.Decoder;
using BEditor.Media;
using BEditor.Media.Decoding;

namespace BEditor.Extensions.FFmpeg
{
    public class RegisterdDecoding : IRegisterdDecoding
    {
        public string Name => "FFmpeg";

        public IInputContainer? Open(string file, MediaOptions options)
        {
            return new InputContainer(file, options);
        }

        public IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
            yield return ".wav";
            yield return ".mp3";
            yield return ".wmv";
            yield return ".avi";
            yield return ".webm";
            yield return ".3gp";
            yield return ".3g2";
            yield return ".flv";
            yield return ".mkv";
            yield return ".mov";
            yield return ".ogv";
        }
    }
}