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

/// <summary>Controls whether one caption capability adds or replaces its own slot.</summary>
public enum CaptionCodecRegistrationMode
{
    Add,
    Replace,
}

/// <summary>Registers metadata and file-extension lookup for one caption format.</summary>
public sealed class CaptionCodecDescriptorRegistration
{
    public CaptionCodecDescriptorRegistration(
        CaptionCodecDescriptor descriptor,
        CaptionCodecRegistrationMode mode = CaptionCodecRegistrationMode.Add)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionCodecDescriptor Descriptor { get; }

    public CaptionCodecRegistrationMode Mode { get; }
}

/// <summary>Registers decoding independently from metadata and encoding.</summary>
public sealed class CaptionDecoderRegistration
{
    public CaptionDecoderRegistration(
        CaptionFormatId format,
        ICaptionDecoder decoder,
        CaptionCodecRegistrationMode mode = CaptionCodecRegistrationMode.Add)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        Format = format;
        Decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionFormatId Format { get; }

    public ICaptionDecoder Decoder { get; }

    public CaptionCodecRegistrationMode Mode { get; }
}

/// <summary>Registers encoding independently from metadata and decoding.</summary>
public sealed class CaptionEncoderRegistration
{
    public CaptionEncoderRegistration(
        CaptionFormatId format,
        ICaptionEncoder encoder,
        CaptionCodecRegistrationMode mode = CaptionCodecRegistrationMode.Add)
    {
        if (format.Value.Length == 0)
            throw new ArgumentException("A caption format identifier is required.", nameof(format));
        Format = format;
        Encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionFormatId Format { get; }

    public ICaptionEncoder Encoder { get; }

    public CaptionCodecRegistrationMode Mode { get; }
}
