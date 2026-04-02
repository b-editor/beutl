using Beutl.FFmpegWorker.Encoding;

namespace Beutl.FFmpegWorker;

/// <summary>
/// Worker側ではCodecCacheは不要。ChoicesProviderのコンパイルを通すためのスタブ。
/// Worker側ではChoicesProviderは呼ばれない。
/// </summary>
internal static class FFmpegWorkerCodecCache
{
    public static IReadOnlyList<object> GetVideoCodecs() => [CodecRecord.Default];
    public static IReadOnlyList<object> GetAudioCodecs() => [CodecRecord.Default];
    public static void Invalidate() { }
}
