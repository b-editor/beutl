using System.Text;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Imports and exports provider-independent caption documents as strict UTF-8.
/// </summary>
public sealed class CaptionDocumentSerializer
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly ICaptionCodecProvider _codecs;

    public CaptionDocumentSerializer(ICaptionCodecProvider codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        _codecs = codecs;
    }

    public CaptionImportResult Import(ReadOnlySpan<byte> utf8, CaptionFormatId format)
    {
        string content;
        try
        {
            content = s_utf8.GetString(utf8);
        }
        catch (DecoderFallbackException ex)
        {
            return CaptionImportResult.Failure(
            [
                new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidUtf8,
                    null,
                    $"The input is not valid UTF-8: {ex.Message}"),
            ]);
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content[1..];

        return _codecs.Decode(format, content);
    }

    public byte[] Export(CaptionDocument document, CaptionFormatId format)
    {
        ArgumentNullException.ThrowIfNull(document);
        string content = _codecs.Encode(format, document);

        try
        {
            return s_utf8.GetBytes(content);
        }
        catch (EncoderFallbackException ex)
        {
            throw new CaptionExportException(
                null,
                "The document contains invalid UTF-16 and cannot be encoded as UTF-8.",
                ex);
        }
    }
}
