namespace Beutl.Extensions.AVFoundation;

internal static class AVFFileExtensions
{
    // The video containers the decoder opens and the encoder writes.
    public static IEnumerable<string> Video()
    {
        yield return ".mp4";
        yield return ".mov";
        yield return ".m4v";
        yield return ".avi";
        yield return ".wmv";
        yield return ".sami";
        yield return ".smi";
        yield return ".adts";
        yield return ".asf";
        yield return ".3gp";
        yield return ".3gp2";
        yield return ".3gpp";
    }
}
