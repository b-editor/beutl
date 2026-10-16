namespace Beutl.Editor.Services.Captions;

public sealed class CaptionCodecDescriptor
{
    public CaptionCodecDescriptor(
        CaptionFormatId format,
        IEnumerable<string> fileExtensions,
        int order = 0)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        ArgumentNullException.ThrowIfNull(fileExtensions);
        string[] extensions = fileExtensions.ToArray();
        if (extensions.Length == 0 || extensions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A caption descriptor requires at least one non-empty file extension.",
                nameof(fileExtensions));
        }

        Format = format;
        FileExtensions = Array.AsReadOnly(extensions);
        Order = order;
    }

    public CaptionFormatId Format { get; }

    public IReadOnlyCollection<string> FileExtensions { get; }

    /// <summary>
    /// Determines presentation order. Formats with the same order are sorted by their stable
    /// format identifier.
    /// </summary>
    public int Order { get; }
}

public interface ICaptionDecoder
{
    CaptionImportResult Decode(string content);
}

public interface ICaptionEncoder
{
    string Encode(CaptionDocument document);
}
