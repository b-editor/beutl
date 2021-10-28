
using BEditor.Media.Decoding;

namespace BEditor.Models.Tool
{
    public sealed record ConvertVideoSource(VideoStreamInfo VideoInfo, AudioStreamInfo AudioInfo, string File);
}