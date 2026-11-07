using System.Globalization;
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
                    $"The input is not valid UTF-8: {ex.Message}"),
            ]);
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content[1..];

        return _codecs.Decode(format, content);
    }

    // The bytes of a subtitle file. Import reads UTF-8, but subtitle tools also write UTF-16 and, on Windows, the
    // system's legacy code page.
    internal CaptionImportResult ImportFile(byte[] bytes, CaptionFormatId format)
    {
        if (bytes is [0xFF, 0xFE, ..])
            return Import(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, bytes, 2, bytes.Length - 2), format);
        if (bytes is [0xFE, 0xFF, ..])
            return Import(Encoding.Convert(Encoding.BigEndianUnicode, Encoding.UTF8, bytes, 2, bytes.Length - 2), format);

        CaptionImportResult result = Import(bytes, format);
        if (result.IsSuccess || result.Diagnostics.All(item => item.Kind != CaptionDiagnosticKinds.InvalidUtf8))
            return result;

        // Code page 0 is the system's on Windows, whatever the regional format says. Elsewhere there is none, and the
        // locale is the best guess.
        Encoding legacy = CodePagesEncodingProvider.Instance.GetEncoding(0)
            ?? CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage)
            ?? Encoding.Latin1;
        return Import(Encoding.Convert(legacy, Encoding.UTF8, bytes), format);
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
