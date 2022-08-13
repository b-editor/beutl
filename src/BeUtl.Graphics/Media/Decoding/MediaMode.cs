namespace BeUtl.Media.Decoding;

[Flags]
public enum MediaMode
{
    Video = 0b1,
    Audio = 0b10,
    AudioVideo = Video | Audio,
}
