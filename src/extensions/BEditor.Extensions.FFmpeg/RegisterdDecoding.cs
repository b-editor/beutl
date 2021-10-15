using System.Collections.Generic;

using BEditor.Extensions.FFmpeg.Decoder;
using BEditor.Media;
using BEditor.Media.Decoding;

namespace BEditor.Extensions.FFmpeg
{
    public sealed class RegisterdDecoding : IRegisterdDecoding
    {
        public string Name => "FFmpeg";

        public IEnumerable<string> GetSupportedAudioExt()
        {
            yield return ".mp3";
            yield return ".ogg";
            yield return ".wav";
            yield return ".aac";
            yield return ".wma";
            yield return ".m4a";
            yield return ".webm";
            yield return ".opus";
        }

        public IEnumerable<string> GetSupportedVideoExt()
        {
            yield return ".avi";
            yield return ".mov";
            yield return ".wmv";
            yield return ".mp4";
            yield return ".webm";
            yield return ".mkv";
            yield return ".flv";
            yield return ".264";
            yield return ".mpeg";
            yield return ".ts";
            yield return ".mts";
            yield return ".m2ts";
        }

        public IInputContainer? Open(string file, MediaOptions options)
        {
            return new InputContainer(file, options);
        }
    }
}