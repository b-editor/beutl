namespace Beutl.Editor.Services.Captions;

public sealed class CaptionCodecDescriptor
{
    private CaptionCodecDescriptor(CaptionFormatId format)
    {
        Format = format;
        FileExtensions = Array.Empty<string>();
    }

    public CaptionCodecDescriptor(
        CaptionFormatId format,
        IEnumerable<string> fileExtensions)
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
    }

    public CaptionFormatId Format { get; }

    public IReadOnlyCollection<string> FileExtensions { get; }

    internal static CaptionCodecDescriptor Empty(CaptionFormatId format) => new(format);
}

public interface ICaptionDecoder
{
    CaptionImportResult Decode(string content);
}

public interface ICaptionEncoder
{
    string Encode(CaptionDocument document);
}

/// <summary>
/// Registers metadata and whichever independent codec capabilities a format supports.
/// </summary>
public sealed class CaptionCodecContribution
{
    public CaptionCodecContribution(
        CaptionFormatId format,
        CaptionCodecDescriptor? descriptor = null,
        ICaptionDecoder? decoder = null,
        ICaptionEncoder? encoder = null,
        int order = 0)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        if (descriptor is null && decoder is null && encoder is null)
            throw new ArgumentException("A caption contribution must provide a descriptor or codec capability.");
        if (descriptor is not null && descriptor.Format != format)
            throw new ArgumentException("The descriptor format must match the contribution format.", nameof(descriptor));

        Format = format;
        HasDescriptor = descriptor is not null;
        Descriptor = descriptor ?? CaptionCodecDescriptor.Empty(format);
        Decoder = decoder;
        Encoder = encoder;
        Order = order;
    }

    public CaptionFormatId Format { get; }

    /// <summary>
    /// Descriptor metadata for this contribution. Capability-only contributions expose an empty
    /// descriptor and set <see cref="HasDescriptor"/> to <see langword="false"/>.
    /// </summary>
    public CaptionCodecDescriptor Descriptor { get; }

    public bool HasDescriptor { get; }

    public ICaptionDecoder? Decoder { get; }

    public ICaptionEncoder? Encoder { get; }

    /// <summary>
    /// Determines presentation order. Formats with the same order are sorted by their stable
    /// format identifier.
    /// </summary>
    public int Order { get; }
}

public enum CaptionCodecRegistrationMode
{
    Add,
    Merge,
    Replace,
}

/// <summary>
/// Applies one codec contribution with explicit collision semantics.
/// </summary>
public sealed class CaptionCodecRegistration
{
    public CaptionCodecRegistration(
        CaptionCodecContribution contribution,
        CaptionCodecRegistrationMode mode = CaptionCodecRegistrationMode.Add)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Contribution = contribution;
        Mode = mode;
    }

    public CaptionCodecContribution Contribution { get; }

    public CaptionCodecRegistrationMode Mode { get; }
}
