using System.Net;
using System.Text;

namespace Beutl.Editor.Services.Captions;

public sealed class WebVttCaptionCodec : ICaptionDecoder, ICaptionEncoder
{
    public CaptionFormatId Format => CaptionFormats.WebVtt;

    public CaptionImportResult Decode(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string[] lines = CaptionTextUtilities.GetLines(content);
        if (lines.Length == 0 || !IsHeader(lines[0]))
        {
            return CaptionImportResult.Failure(
            [
                new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidHeader,
                    1,
                    "A WebVTT document must begin with WEBVTT."),
            ]);
        }

        var cues = new List<CaptionCue>();
        var errors = new List<CaptionDiagnostic>();
        int index = 1;

        while (index < lines.Length && lines[index].Length > 0)
        {
            if (lines[index].Contains("-->", StringComparison.Ordinal))
                break;
            index++;
        }

        while (index < lines.Length)
        {
            while (index < lines.Length && lines[index].Length == 0)
                index++;

            if (index >= lines.Length)
                break;

            if (IsBlock(lines[index], "NOTE")
                || IsBlock(lines[index], "STYLE")
                || IsBlock(lines[index], "REGION"))
            {
                SkipBlock(lines, ref index);
                continue;
            }

            int cueLine = index + 1;
            if (!lines[index].Contains("-->", StringComparison.Ordinal))
            {
                index++;
                if (index >= lines.Length || !lines[index].Contains("-->", StringComparison.Ordinal))
                {
                    errors.Add(new CaptionDiagnostic(
                        CaptionDiagnosticKinds.InvalidStructure,
                        cueLine,
                        "The WebVTT cue identifier is not followed by a timing line."));
                    SkipBlock(lines, ref index);
                    continue;
                }
            }

            string timingLine = lines[index];
            int timingLineNumber = index + 1;
            index++;
            bool timingIsValid = TryParseTiming(
                timingLine,
                timingLineNumber,
                errors,
                out TimeSpan start,
                out TimeSpan end);

            int textStart = index;
            while (index < lines.Length && lines[index].Length > 0)
                index++;

            if (timingIsValid)
            {
                string payload = string.Join('\n', lines[textStart..index]);
                ParsedWebVttText parsed = ParseText(payload);
                CaptionMetadata metadata = parsed.Classes is null
                    ? CaptionMetadata.Empty
                    : CaptionMetadata.Empty.Set(CaptionMetadataKeys.WebVttClasses, parsed.Classes);
                cues.Add(new CaptionCue(
                    start,
                    end,
                    parsed.Text,
                    parsed.Speaker,
                    parsed.Language,
                    metadata));
            }
        }

        var document = new CaptionDocument(cues);
        return errors.Count == 0 || cues.Count > 0
            ? CaptionImportResult.Imported(document, errors)
            : CaptionImportResult.Failure(errors);
    }

    public string Encode(CaptionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder("WEBVTT\r\n\r\n");
        for (int i = 0; i < document.Count; i++)
        {
            CaptionCue cue = document[i];
            CaptionCodecUtilities.EnsureCueCanExport(cue, i);
            CaptionCodecUtilities.EnsureNoBlankCueLines(cue.Text, i, Format);
            ValidateMetadata(cue, i);
            (TimeSpan start, TimeSpan end) = CaptionCodecUtilities.QuantizeCue(
                cue,
                i,
                TimeSpan.TicksPerMillisecond);

            builder.Append(CaptionCodecUtilities.FormatWebVttTime(start));
            builder.Append(" --> ");
            builder.Append(CaptionCodecUtilities.FormatWebVttTime(end));
            builder.Append("\r\n");
            builder.Append(EncodeText(cue).Replace("\n", "\r\n", StringComparison.Ordinal));
            builder.Append("\r\n\r\n");
        }

        return builder.ToString();
    }

    private static ParsedWebVttText ParseText(string payload)
    {
        string? speaker = null;
        string? language = null;
        string? style = null;
        int position = 0;
        while (position < payload.Length && payload[position] == '<')
        {
            int close = payload.IndexOf('>', position + 1);
            if (close < 0)
                break;

            string tag = payload[(position + 1)..close].Trim();
            if (tag.StartsWith("v ", StringComparison.OrdinalIgnoreCase))
            {
                speaker ??= WebUtility.HtmlDecode(tag[2..].Trim());
            }
            else if (tag.StartsWith("lang ", StringComparison.OrdinalIgnoreCase))
            {
                language ??= WebUtility.HtmlDecode(tag[5..].Trim());
            }
            else if (tag.StartsWith("c.", StringComparison.OrdinalIgnoreCase))
            {
                style ??= WebUtility.HtmlDecode(tag[2..].Trim());
            }
            else
            {
                break;
            }

            position = close + 1;
        }

        var plainText = new StringBuilder(payload.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            if (payload[i] == '<')
            {
                int close = payload.IndexOf('>', i + 1);
                if (close >= 0 && IsRecognizedCueTag(payload.AsSpan(i + 1, close - i - 1)))
                {
                    i = close;
                    continue;
                }
            }

            plainText.Append(payload[i]);
        }

        return new ParsedWebVttText(
            WebUtility.HtmlDecode(plainText.ToString()),
            EmptyToNull(speaker),
            EmptyToNull(language),
            EmptyToNull(style));
    }

    private static string EncodeText(CaptionCue cue)
    {
        string? classes = cue.Metadata.GetValueOrDefault(CaptionMetadataKeys.WebVttClasses);
        var builder = new StringBuilder();
        if (cue.Speaker is not null)
            builder.Append("<v ").Append(WebUtility.HtmlEncode(cue.Speaker)).Append('>');
        if (cue.Language is not null)
            builder.Append("<lang ").Append(cue.Language).Append('>');
        if (classes is not null)
            builder.Append("<c.").Append(classes).Append('>');

        builder.Append(WebUtility.HtmlEncode(CaptionTextUtilities.NormalizeLineEndings(cue.Text)));

        if (classes is not null)
            builder.Append("</c>");
        if (cue.Language is not null)
            builder.Append("</lang>");
        if (cue.Speaker is not null)
            builder.Append("</v>");
        return builder.ToString();
    }

    private static void ValidateMetadata(CaptionCue cue, int cueIndex)
    {
        string? classes = cue.Metadata.GetValueOrDefault(CaptionMetadataKeys.WebVttClasses);
        if (cue.Speaker?.IndexOfAny('\r', '\n') >= 0)
        {
            throw new CaptionExportException(
                cueIndex,
                "A WebVTT voice annotation cannot contain a line break.");
        }

        if (cue.Language is not null && !cue.Language.All(IsLanguageCharacter))
        {
            throw new CaptionExportException(
                cueIndex,
                "A WebVTT language annotation may contain only ASCII letters, digits, and hyphens.");
        }

        if (classes is not null && !classes.All(IsClassCharacter))
        {
            throw new CaptionExportException(
                cueIndex,
                "A WebVTT class annotation may contain only letters, digits, periods, underscores, and hyphens.");
        }
    }

    private static bool TryParseTiming(
        string line,
        int lineNumber,
        List<CaptionDiagnostic> errors,
        out TimeSpan start,
        out TimeSpan end)
    {
        start = default;
        end = default;
        if (!CaptionCodecUtilities.TrySplitTimingLine(line, out string startText, out string endText)
            || !CaptionCodecUtilities.TryParseWebVttTime(startText, out start)
            || !CaptionCodecUtilities.TryParseWebVttTime(endText, out end)
            || end <= start)
        {
            errors.Add(new CaptionDiagnostic(
                CaptionDiagnosticKinds.InvalidTiming,
                lineNumber,
                "The WebVTT timing is malformed or does not end after it starts."));
            return false;
        }

        return true;
    }

    private static bool IsHeader(string value)
        => value.Equals("WEBVTT", StringComparison.Ordinal)
           || (value.StartsWith("WEBVTT", StringComparison.Ordinal)
               && value.Length > 6
               && char.IsWhiteSpace(value[6]));

    private static bool IsBlock(string value, string name)
        => value.Equals(name, StringComparison.Ordinal)
           || (value.StartsWith(name, StringComparison.Ordinal)
               && value.Length > name.Length
               && char.IsWhiteSpace(value[name.Length]));

    private static void SkipBlock(string[] lines, ref int index)
    {
        while (index < lines.Length && lines[index].Length > 0)
            index++;
    }

    private static bool IsRecognizedCueTag(ReadOnlySpan<char> rawTag)
    {
        ReadOnlySpan<char> tag = rawTag.Trim();
        if (!tag.IsEmpty && tag[0] == '/')
            tag = tag[1..].TrimStart();

        int annotation = tag.IndexOfAny(' ', '.');
        ReadOnlySpan<char> name = annotation >= 0 ? tag[..annotation] : tag;
        if (name.Equals("b", StringComparison.OrdinalIgnoreCase)
            || name.Equals("i", StringComparison.OrdinalIgnoreCase)
            || name.Equals("u", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ruby", StringComparison.OrdinalIgnoreCase)
            || name.Equals("rt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("v", StringComparison.OrdinalIgnoreCase)
            || name.Equals("lang", StringComparison.OrdinalIgnoreCase)
            || name.Equals("c", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CaptionCodecUtilities.TryParseWebVttTime(tag.ToString(), out _);
    }

    private static bool IsLanguageCharacter(char value)
        => value is >= 'a' and <= 'z'
           or >= 'A' and <= 'Z'
           or >= '0' and <= '9'
           or '-';

    private static bool IsClassCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '.' or '_' or '-';

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record ParsedWebVttText(
        string Text,
        string? Speaker,
        string? Language,
        string? Classes);
}
