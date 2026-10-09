using System.IO.Enumeration;

using Avalonia.Platform.Storage;

namespace Beutl.Controls.PropertyEditors;

// Decides whether a dropped file belongs to a picker file type.
// Patterns, when present, decide alone, as they do on the Windows, macOS and portal pickers, so adding
// MIME types or UTIs to a pattern filter never widens what can be dropped. Without patterns the file's
// extension is looked up in the catalog below and matched against the MIME types and UTIs. A file the
// catalog does not know passes only the catch-all identifiers: an unverifiable drop is refused, not guessed.
internal static class FilePickerFileTypeMatcher
{
    private static readonly string[] s_anyMimeTypes = ["*/*", "application/octet-stream"];
    private static readonly string[] s_anyUniformTypeIdentifiers = ["public.item", "public.data"];

    // Conformance follows macOS's UTType database; the MIME types add the shared-mime-info names
    // Linux pickers use. Types macOS does not declare have no UTI.
    private static readonly FileType[] s_types =
    [
        new("public.image", "public.data public.content"),
        new("public.heif-standard", "public.image"),
        new("public.png", "public.image", "png", "image/png"),
        new("public.jpeg", "public.image", "jpg jpeg jpe", "image/jpeg image/jpg image/pjpeg"),
        new("com.compuserve.gif", "public.image", "gif", "image/gif"),
        new("com.microsoft.bmp", "public.image", "bmp dib", "image/bmp image/x-bmp image/x-ms-bmp"),
        new("org.webmproject.webp", "public.image", "webp", "image/webp"),
        new("public.tiff", "public.image", "tif tiff", "image/tiff"),
        new("public.heic", "public.heif-standard", "heic", "image/heic image/heif"),
        new("public.heif", "public.heif-standard", "heif hif", "image/heif image/heic"),
        new("public.avif", "public.heif-standard", "avif", "image/avif"),
        new("com.microsoft.ico", "public.image", "ico", "image/vnd.microsoft.icon image/x-icon"),
        new("public.svg-image", "public.image public.xml", "svg svgz", "image/svg+xml"),

        new("public.audiovisual-content", "public.data public.content"),
        new("public.movie", "public.audiovisual-content"),
        new("public.mpeg-4", "public.movie", "mp4 mpg4", "video/mp4 video/mp4v-es"),
        new("com.apple.m4v-video", "public.mpeg-4", "m4v", "video/x-m4v"),
        new("com.apple.quicktime-movie", "public.movie", "mov qt", "video/quicktime"),
        new("org.webmproject.webm", "public.movie", "webm", "video/webm audio/webm"),
        new("public.avi", "public.movie", "avi", "video/avi video/msvideo video/x-msvideo video/vnd.avi"),
        new("public.mpeg", "public.movie", "mpg mpeg mpe", "video/mpeg video/mpg video/x-mpeg"),
        new("com.microsoft.windows-media-wmv", "com.microsoft.advanced-systems-format public.movie", "wmv", "video/x-ms-wmv"),
        new(null, "", "mkv", "video/matroska video/x-matroska"),

        new("public.audio", "public.audiovisual-content"),
        new("public.mp3", "public.audio", "mp3", "audio/mpeg audio/mp3 audio/x-mp3 audio/x-mpeg"),
        new("com.microsoft.waveform-audio", "public.audio", "wav wave", "audio/wav audio/wave audio/vnd.wave audio/x-wav"),
        new("public.aifc-audio", "public.audio"),
        new("public.aiff-audio", "public.aifc-audio", "aif aiff", "audio/aiff audio/x-aiff"),
        new("public.mpeg-4-audio", "public.audio", "", "audio/mp4"),
        new("com.apple.m4a-audio", "public.mpeg-4-audio", "m4a", "audio/x-m4a audio/m4a"),
        new("public.aac-audio", "public.audio", "aac", "audio/aac audio/x-aac"),
        new("org.xiph.flac", "public.audio", "flac", "audio/flac audio/x-flac"),
        new("org.xiph.ogg-audio", "public.audio", "ogg oga opus", "audio/ogg audio/opus"),

        new("public.text", "public.data public.content"),
        new("public.plain-text", "public.text", "txt text", "text/plain"),
        new("public.comma-separated-values-text", "public.delimited-values-text public.plain-text", "csv", "text/csv text/comma-separated-values"),
        new("public.json", "public.text", "json", "application/json"),
        new("public.xml", "public.text", "xml", "application/xml text/xml"),
        new("org.w3.webvtt", "public.text", "vtt", "text/vtt"),
        new(null, "", "srt", "application/x-subrip"),
        new(null, "", "ass ssa", "text/x-ssa"),

        new("public.3d-content", "public.content"),
        new("org.khronos.gltf", "public.3d-content public.json", "gltf", "model/gltf+json"),
        new("org.khronos.glb", "public.3d-content public.data", "glb", "model/gltf-binary"),
        new("public.geometry-definition-format", "public.3d-content public.text", "obj", "model/obj"),
        new("public.standard-tesselated-geometry-format", "public.3d-content public.data", "stl", "model/stl"),

        new("public.font", "public.data"),
        new("public.truetype-font", "public.font"),
        new("public.truetype-ttf-font", "public.truetype-font", "ttf", "font/ttf"),
        new("public.opentype-font", "public.font", "otf", "font/otf"),
        new(null, "", "woff", "font/woff"),
        new(null, "", "woff2", "font/woff2"),
    ];

    private static readonly Dictionary<string, FileType> s_byExtension = s_types
        .SelectMany(type => type.Extensions.Select(extension => KeyValuePair.Create(extension, type)))
        .ToDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, FileType> s_byUniformTypeIdentifier = s_types
        .Where(type => type.Uti != null)
        .ToDictionary(type => type.Uti!, StringComparer.OrdinalIgnoreCase);

    public static bool Matches(FilePickerFileType filter, string fileName)
    {
        if (filter.Patterns is { Count: > 0 } patterns)
        {
            return patterns.Any(pattern => pattern is "*" or "*.*"
                || FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true));
        }

        FileType? type = s_byExtension.GetValueOrDefault(Path.GetExtension(fileName).TrimStart('.'));
        return filter.MimeTypes?.Any(mimeType => MatchesMimeType(type, mimeType)) == true
            || filter.AppleUniformTypeIdentifiers?.Any(uti => ConformsTo(type, uti)) == true;
    }

    private static bool MatchesMimeType(FileType? type, string mimeType)
    {
        if (s_anyMimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase)) return true;
        if (type == null) return false;

        IEnumerable<string> mimeTypes = SelfAndSupertypes(type).SelectMany(t => t.MimeTypes);
        return mimeType.EndsWith("/*", StringComparison.Ordinal)
            ? mimeTypes.Any(t => t.StartsWith(mimeType[..^1], StringComparison.OrdinalIgnoreCase))
            : mimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ConformsTo(FileType? type, string uti)
    {
        if (s_anyUniformTypeIdentifiers.Contains(uti, StringComparer.OrdinalIgnoreCase)) return true;
        if (type == null) return false;

        return SelfAndSupertypes(type).Any(t =>
            string.Equals(t.Uti, uti, StringComparison.OrdinalIgnoreCase)
            || t.Supertypes.Contains(uti, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<FileType> SelfAndSupertypes(FileType type)
    {
        return type.Supertypes
            .Select(uti => s_byUniformTypeIdentifier.GetValueOrDefault(uti))
            .OfType<FileType>()
            .SelectMany(SelfAndSupertypes)
            .Prepend(type);
    }

    private sealed class FileType(string? uti, string supertypes, string extensions = "", string mimeTypes = "")
    {
        public string? Uti { get; } = uti;

        public string[] Supertypes { get; } = Split(supertypes);

        public string[] Extensions { get; } = Split(extensions);

        public string[] MimeTypes { get; } = Split(mimeTypes);

        private static string[] Split(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
