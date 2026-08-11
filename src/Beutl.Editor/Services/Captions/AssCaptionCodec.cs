using System.Text;

namespace Beutl.Editor.Services.Captions;

public sealed class AssCaptionCodec : ICaptionDecoder, ICaptionEncoder
{
    private const string LanguageEffectPrefix = "beutl-language=";

    public CaptionFormatId Format => CaptionFormats.Ass;

    public CaptionImportResult Decode(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string[] lines = CaptionTextUtilities.GetLines(content);
        var cues = new List<CaptionCue>();
        var errors = new List<CaptionDiagnostic>();
        string[]? format = null;
        bool inEvents = false;
        bool foundEvents = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmedLine = lines[i].Trim();
            if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
            {
                inEvents = trimmedLine.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                foundEvents |= inEvents;
                continue;
            }

            if (!inEvents || trimmedLine.Length == 0 || trimmedLine.StartsWith(';'))
                continue;

            string directiveLine = lines[i].TrimStart();
            if (TryGetDirective(directiveLine, "Format", out string formatValue))
            {
                string[] candidate = formatValue.Split(',').Select(field => field.Trim()).ToArray();
                if (!IsSupportedFormat(candidate))
                {
                    errors.Add(new CaptionDiagnostic(
                        CaptionDiagnosticKinds.InvalidStructure,
                        i + 1,
                        "ASS/SSA event format must contain Start, End, and a final Text field."));
                    format = null;
                }
                else
                {
                    format = candidate;
                }

                continue;
            }

            if (!TryGetDirective(directiveLine, "Dialogue", out string dialogue))
                continue;

            if (format is null)
            {
                errors.Add(new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidStructure,
                    i + 1,
                    "ASS/SSA Dialogue appears before a supported event Format line."));
                continue;
            }

            string[] fields = dialogue.Split(',', format.Length, StringSplitOptions.None);
            if (fields.Length != format.Length)
            {
                errors.Add(new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidStructure,
                    i + 1,
                    "ASS/SSA Dialogue has fewer fields than its event Format line."));
                continue;
            }

            int startIndex = FindField(format, "Start");
            int endIndex = FindField(format, "End");
            int textIndex = FindField(format, "Text");
            if (!CaptionCodecUtilities.TryParseAssTime(fields[startIndex], out TimeSpan start)
                || !CaptionCodecUtilities.TryParseAssTime(fields[endIndex], out TimeSpan end)
                || end <= start)
            {
                errors.Add(new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidTiming,
                    i + 1,
                    "ASS/SSA timing must use H:MM:SS.cc and have an end after its start."));
                continue;
            }

            int speakerIndex = FindOptionalField(format, "Name", "Actor");
            int styleIndex = FindOptionalField(format, "Style");
            int effectIndex = FindOptionalField(format, "Effect");
            string? speaker = speakerIndex >= 0 ? EmptyToNull(fields[speakerIndex].Trim()) : null;
            string? style = styleIndex >= 0 ? EmptyToNull(fields[styleIndex].Trim()) : null;
            string? language = effectIndex >= 0 ? DecodeLanguage(fields[effectIndex].Trim()) : null;

            CaptionMetadata metadata = style is null
                ? CaptionMetadata.Empty
                : CaptionMetadata.Empty.Set(CaptionMetadataKeys.AssStyle, style);
            cues.Add(new CaptionCue(
                start,
                end,
                DecodeText(fields[textIndex]),
                speaker,
                language,
                metadata));
        }

        if (!foundEvents)
        {
            errors.Add(new CaptionDiagnostic(
                CaptionDiagnosticKinds.InvalidHeader,
                null,
                "An ASS/SSA document must contain an [Events] section."));
        }
        else if (format is null && !errors.Any(error => error.Kind == CaptionDiagnosticKinds.InvalidStructure))
        {
            errors.Add(new CaptionDiagnostic(
                CaptionDiagnosticKinds.InvalidStructure,
                null,
                "The ASS/SSA [Events] section must contain a supported Format line."));
        }

        var document = new CaptionDocument(cues);
        return errors.Count == 0 || cues.Count > 0
            ? CaptionImportResult.Imported(document, errors)
            : CaptionImportResult.Failure(errors);
    }

    public string Encode(CaptionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var styles = new List<string> { "Default" };
        var seenStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Default" };
        for (int i = 0; i < document.Count; i++)
        {
            CaptionCue cue = document[i];
            string? style = cue.Metadata.GetValueOrDefault(CaptionMetadataKeys.AssStyle);
            CaptionCodecUtilities.EnsureCueCanExport(cue, i);
            EnsureAssField(cue.Speaker, i, "speaker");
            EnsureAssField(style, i, "style");
            if (cue.Text.IndexOfAny('{', '}') >= 0)
            {
                throw new CaptionExportException(
                    i,
                    "ASS/SSA cannot safely represent literal braces because they delimit override blocks.");
            }

            if (!string.IsNullOrEmpty(style) && seenStyles.Add(style))
                styles.Add(style);
        }

        var builder = new StringBuilder();
        builder.Append("[Script Info]\r\n");
        builder.Append("ScriptType: v4.00+\r\n");
        builder.Append("Collisions: Normal\r\n");
        builder.Append("WrapStyle: 0\r\n");
        builder.Append("ScaledBorderAndShadow: yes\r\n\r\n");
        builder.Append("[V4+ Styles]\r\n");
        builder.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n");
        foreach (string style in styles)
        {
            builder.Append("Style: ").Append(style);
            builder.Append(",Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,20,20,20,1\r\n");
        }

        builder.Append("\r\n[Events]\r\n");
        builder.Append("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n");
        for (int i = 0; i < document.Count; i++)
        {
            CaptionCue cue = document[i];
            (TimeSpan start, TimeSpan end) = CaptionCodecUtilities.QuantizeCue(
                cue,
                i,
                TimeSpan.TicksPerSecond / 100);
            string style = cue.Metadata.GetValueOrDefault(CaptionMetadataKeys.AssStyle) ?? "Default";
            string effect = cue.Language is null
                ? string.Empty
                : LanguageEffectPrefix + Uri.EscapeDataString(cue.Language);

            builder.Append("Dialogue: 0,");
            builder.Append(CaptionCodecUtilities.FormatAssTime(start)).Append(',');
            builder.Append(CaptionCodecUtilities.FormatAssTime(end)).Append(',');
            builder.Append(style).Append(',');
            builder.Append(cue.Speaker).Append(",0,0,0,");
            builder.Append(effect).Append(',');
            builder.Append(EncodeText(cue.Text)).Append("\r\n");
        }

        return builder.ToString();
    }

    private static string DecodeText(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == '\\' && i + 1 < value.Length)
            {
                char escape = value[i + 1];
                if (escape == '\\')
                {
                    builder.Append('\\');
                    i++;
                    continue;
                }

                if (escape is 'N' or 'n')
                {
                    builder.Append('\n');
                    i++;
                    continue;
                }

                if (escape == 'h')
                {
                    builder.Append('\u00A0');
                    i++;
                    continue;
                }
            }

            if (character == '{')
            {
                int close = value.IndexOf('}', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }

            if (character == '<')
            {
                int close = value.IndexOf('>', i + 1);
                if (close >= 0 && IsLegacyFormattingTag(value.AsSpan(i + 1, close - i - 1)))
                {
                    i = close;
                    continue;
                }
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string EncodeText(string value)
    {
        string normalized = CaptionTextUtilities.NormalizeLineEndings(value);
        var builder = new StringBuilder(normalized.Length);
        foreach (char character in normalized)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\N");
                    break;
                case '\u00A0':
                    builder.Append("\\h");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryGetDirective(string line, string name, out string value)
    {
        if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && line.Length > name.Length
            && line[name.Length] == ':')
        {
            value = line[(name.Length + 1)..].TrimStart();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsSupportedFormat(string[] fields)
        => fields.Length > 0
           && fields[^1].Equals("Text", StringComparison.OrdinalIgnoreCase)
           && FindField(fields, "Start") >= 0
           && FindField(fields, "End") >= 0;

    private static int FindField(string[] fields, string name)
        => Array.FindIndex(fields, field => field.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static int FindOptionalField(string[] fields, params string[] names)
    {
        foreach (string name in names)
        {
            int index = FindField(fields, name);
            if (index >= 0)
                return index;
        }

        return -1;
    }

    private static string? DecodeLanguage(string effect)
    {
        if (!effect.StartsWith(LanguageEffectPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return EmptyToNull(Uri.UnescapeDataString(effect[LanguageEffectPrefix.Length..]));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static void EnsureAssField(string? value, int cueIndex, string fieldName)
    {
        if (value?.IndexOfAny(',', '\r', '\n') >= 0)
        {
            throw new CaptionExportException(
                cueIndex,
                $"ASS/SSA cue {fieldName} cannot contain commas or line breaks.");
        }
    }

    private static bool IsLegacyFormattingTag(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> tag = value.Trim();
        if (!tag.IsEmpty && tag[0] == '/')
            tag = tag[1..];
        return tag.Equals("b", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("i", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("u", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("s", StringComparison.OrdinalIgnoreCase);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
