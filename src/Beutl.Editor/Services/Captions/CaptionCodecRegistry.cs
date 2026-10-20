using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Describes one caption format whose descriptor is currently registered. Decoder-only and
/// encoder-only formats remain available by exact identifier but do not appear in this metadata
/// view or file-extension lookup.
/// </summary>
public sealed class CaptionCodecInfo
{
    // The registry has already normalized and checked the descriptor's extensions.
    internal CaptionCodecInfo(
        CaptionCodecDescriptor descriptor,
        string[] normalizedExtensions,
        bool canDecode,
        bool canEncode)
    {
        Format = descriptor.Format;
        FileExtensions = Array.AsReadOnly(normalizedExtensions);
        CanDecode = canDecode;
        CanEncode = canEncode;
        Order = descriptor.Order;
    }

    public CaptionCodecInfo(
        CaptionFormatId format,
        IEnumerable<string> fileExtensions,
        bool canDecode,
        bool canEncode,
        int order = 0)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        ArgumentNullException.ThrowIfNull(fileExtensions);
        string[] normalizedExtensions = fileExtensions.Select(NormalizeExtension).ToArray();
        if (normalizedExtensions.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != normalizedExtensions.Length)
        {
            throw new ArgumentException(
                "A caption codec cannot describe the same file extension more than once.",
                nameof(fileExtensions));
        }

        Format = format;
        FileExtensions = Array.AsReadOnly(normalizedExtensions);
        CanDecode = canDecode;
        CanEncode = canEncode;
        Order = order;
    }

    public CaptionFormatId Format { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    public bool CanDecode { get; }

    public bool CanEncode { get; }

    public int Order { get; }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim();
        return normalized[0] == '.' ? normalized : '.' + normalized;
    }
}

/// <summary>
/// Resolves caption capabilities by exact format identifier. Only formats with registered
/// descriptors appear in <see cref="Codecs"/> and file-extension lookup.
/// </summary>
public interface ICaptionCodecProvider
{
    ICoreReadOnlyList<CaptionCodecInfo> Codecs { get; }

    bool TryGet(
        CaptionFormatId format,
        [NotNullWhen(true)] out CaptionCodecInfo? codec);

    bool TryGetByFileName(
        string fileName,
        [NotNullWhen(true)] out CaptionCodecInfo? codec);

    CaptionImportResult Decode(CaptionFormatId format, string content);

    string Encode(CaptionFormatId format, CaptionDocument document);
}

public sealed class CaptionCodecRegistry : ICaptionCodecProvider
{
    private readonly CoreList<CaptionCodecInfo> _codecs = [];
    private readonly Dictionary<CaptionFormatId, CaptionCodecInfo> _codecsByFormat;
    private readonly Dictionary<string, CaptionFormatId> _formatsByExtension =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CaptionFormatId, ICaptionDecoder> _decoders;
    private readonly Dictionary<CaptionFormatId, ICaptionEncoder> _encoders;

    internal CaptionCodecRegistry(
        IEnumerable<CaptionCodecDescriptor> descriptors,
        IReadOnlyDictionary<CaptionFormatId, ICaptionDecoder> decoders,
        IReadOnlyDictionary<CaptionFormatId, ICaptionEncoder> encoders)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _decoders = new Dictionary<CaptionFormatId, ICaptionDecoder>(decoders);
        _encoders = new Dictionary<CaptionFormatId, ICaptionEncoder>(encoders);
        var codecs = new List<CaptionCodecInfo>();
        foreach (CaptionCodecDescriptor descriptor in descriptors)
        {
            var extensions = new List<string>(descriptor.FileExtensions.Count);
            foreach (string extension in descriptor.FileExtensions)
            {
                string normalized = NormalizeExtension(extension);
                if (!_formatsByExtension.TryAdd(normalized, descriptor.Format))
                {
                    throw new ArgumentException(
                        $"File extension '{normalized}' is registered more than once.",
                        nameof(descriptors));
                }

                extensions.Add(normalized);
            }

            codecs.Add(new CaptionCodecInfo(
                descriptor,
                [.. extensions],
                _decoders.ContainsKey(descriptor.Format),
                _encoders.ContainsKey(descriptor.Format)));
        }

        CaptionCodecInfo[] ordered = codecs
            .OrderBy(codec => codec.Order)
            .ThenBy(codec => codec.Format.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _codecsByFormat = ordered.ToDictionary(codec => codec.Format);
        _codecs.Replace(ordered);
    }

    public ICoreReadOnlyList<CaptionCodecInfo> Codecs => _codecs;

    public bool TryGet(
        CaptionFormatId format,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
        => _codecsByFormat.TryGetValue(format, out codec);

    public bool TryGetByFileName(
        string fileName,
        [NotNullWhen(true)] out CaptionCodecInfo? codec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        KeyValuePair<string, CaptionFormatId>? bestMatch = null;
        foreach (KeyValuePair<string, CaptionFormatId> candidate in _formatsByExtension)
        {
            if (fileName.EndsWith(candidate.Key, StringComparison.OrdinalIgnoreCase)
                && (bestMatch is null || candidate.Key.Length > bestMatch.Value.Key.Length))
            {
                bestMatch = candidate;
            }
        }

        if (bestMatch is { } match)
            return _codecsByFormat.TryGetValue(match.Value, out codec);

        codec = null;
        return false;
    }

    public CaptionImportResult Decode(CaptionFormatId format, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!_decoders.TryGetValue(format, out ICaptionDecoder? decoder))
            throw new NotSupportedException($"Caption format '{format}' does not support decoding.");

        return decoder.Decode(content)
               ?? throw new InvalidOperationException(
                   $"Caption decoder '{format}' returned a null import result.");
    }

    public string Encode(CaptionFormatId format, CaptionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_encoders.TryGetValue(format, out ICaptionEncoder? encoder))
            throw new NotSupportedException($"Caption format '{format}' does not support encoding.");

        return encoder.Encode(document)
               ?? throw new InvalidOperationException(
                   $"Caption encoder '{format}' returned null export content.");
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim();
        if (normalized[0] != '.')
            normalized = '.' + normalized;

        if (normalized.Length == 1
            || normalized.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0)
        {
            throw new ArgumentException($"'{extension}' is not a valid file extension.", nameof(extension));
        }

        return normalized;
    }
}
