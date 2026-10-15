namespace Beutl.Extensions.AVFoundation;

internal static class AVFFileExtensions
{
    // The containers AVAssetWriter writes, each with its own file type (Writer.swift).
    public static IEnumerable<string> Encodable()
    {
        yield return ".mp4";
        yield return ".mov";
        yield return ".m4v";
    }

    // The video containers the decoder opens. It keeps the longer list: earlier versions of the encoder wrote
    // MP4 data under all of these names.
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
