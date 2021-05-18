using System.IO;

using BEditor.Extensions.FFmpeg.Decoder;
using BEditor.Extensions.FFmpeg.Encoding;
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.Encoding;

namespace BEditor.Extensions.FFmpeg
{
    public class DecoderBuilder : IDecoderBuilder
    {
        public string Name => "FFmpeg";
        public string[] SupportFormats { get; } = new[]
        {
            "Container3GP",
            "Container3GP2",
            "ASF",
            "AVI",
            "FLV",
            "MKV",
            "MOV",
            "MP4",
            "Ogg",
            "WebM",
        };

        public bool IsSupported(string file)
        {
            return Path.GetExtension(file) is ".3gp" or ".3g2" or ".wmv" or ".avi" or
                ".flv" or ".mkv" or ".mov" or ".mp4" or ".ogv" or ".webm" or ".wav" or ".mp3";
        }

        public IInputContainer? Open(string file, MediaOptions options)
        {
            return new InputContainer(file, options);
        }
    }
}