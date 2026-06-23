using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// The codec + output-file snapshot a codec property editor queries the FFmpeg worker with. Both the
/// worker request and the cache key derive from one instance so they cannot diverge if the underlying
/// settings mutate mid-flight.
/// </summary>
internal readonly record struct CodecQueryParams(string? CodecName, string? OutputFile);

/// <summary>
/// Shared snapshot + cache-key derivation for the codec property editors (pixel format / audio format /
/// sample rate), so the three editors cannot drift apart on how they key the option cache.
/// </summary>
internal static class CodecOptionQuery
{
    /// <summary>
    /// Snapshots the codec and output file. The Default codec maps to a null name so the worker
    /// enumerates every option rather than filtering to a specific codec.
    /// </summary>
    public static CodecQueryParams Create(CodecRecord codec, string? outputFile)
        => new(codec.Equals(CodecRecord.Default) ? null : codec.Name, outputFile);

    /// <summary>
    /// Builds the option-cache key. NUL cannot appear in a codec name or path, so keys never collide; null
    /// maps to a placeholder distinct from empty string so the two never alias.
    /// </summary>
    public static string BuildCacheKey(CodecQueryParams query)
        => $"{query.CodecName ?? "<default>"}\0{query.OutputFile ?? "<null>"}";
}
