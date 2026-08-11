using System.Text;

namespace Beutl.Editor.Services.Captions;

public sealed class SrtCaptionCodec : ICaptionDecoder, ICaptionEncoder
{
    public CaptionFormatId Format => CaptionFormats.Srt;

    public CaptionImportResult Decode(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string[] lines = CaptionTextUtilities.GetLines(content);
        var cues = new List<CaptionCue>();
        var errors = new List<CaptionDiagnostic>();
        int index = 0;

        while (index < lines.Length)
        {
            while (index < lines.Length && lines[index].Length == 0)
                index++;

            if (index >= lines.Length)
                break;

            int blockLine = index + 1;
            string timingLine;
            int timingLineNumber;
            if (lines[index].Contains("-->", StringComparison.Ordinal))
            {
                timingLine = lines[index];
                timingLineNumber = index + 1;
                index++;
            }
            else
            {
                if (!IsCueNumber(lines[index]))
                {
                    errors.Add(new CaptionDiagnostic(
                        CaptionDiagnosticKinds.InvalidStructure,
                        blockLine,
                        "Expected a numeric SRT cue identifier or timing line."));
                    SkipBlock(lines, ref index);
                    continue;
                }

                index++;
                if (index >= lines.Length || !lines[index].Contains("-->", StringComparison.Ordinal))
                {
                    errors.Add(new CaptionDiagnostic(
                        CaptionDiagnosticKinds.InvalidStructure,
                        blockLine,
                        "The SRT cue is missing its timing line."));
                    SkipBlock(lines, ref index);
                    continue;
                }

                timingLine = lines[index];
                timingLineNumber = index + 1;
                index++;
            }

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
                cues.Add(new CaptionCue(
                    start,
                    end,
                    string.Join('\n', lines[textStart..index])));
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

        var builder = new StringBuilder();
        for (int i = 0; i < document.Count; i++)
        {
            CaptionCue cue = document[i];
            CaptionCodecUtilities.EnsureCueCanExport(cue, i);
            CaptionCodecUtilities.EnsureNoBlankCueLines(cue.Text, i, Format);
            (TimeSpan start, TimeSpan end) = CaptionCodecUtilities.QuantizeCue(
                cue,
                i,
                TimeSpan.TicksPerMillisecond);

            builder.Append(i + 1).Append("\r\n");
            builder.Append(CaptionCodecUtilities.FormatSrtTime(start));
            builder.Append(" --> ");
            builder.Append(CaptionCodecUtilities.FormatSrtTime(end));
            builder.Append("\r\n");
            builder.Append(CaptionTextUtilities.NormalizeLineEndings(cue.Text).Replace("\n", "\r\n", StringComparison.Ordinal));
            builder.Append("\r\n\r\n");
        }

        return builder.ToString();
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
            || !CaptionCodecUtilities.TryParseSrtTime(startText, out start)
            || !CaptionCodecUtilities.TryParseSrtTime(endText, out end)
            || start < TimeSpan.Zero
            || end <= start)
        {
            errors.Add(new CaptionDiagnostic(
                CaptionDiagnosticKinds.InvalidTiming,
                lineNumber,
                "The SRT timing must use HH:MM:SS,mmm and have an end after its start."));
            return false;
        }

        return true;
    }

    private static bool IsCueNumber(string value)
        => value.Length > 0 && value.All(character => character is >= '0' and <= '9');

    private static void SkipBlock(string[] lines, ref int index)
    {
        while (index < lines.Length && lines[index].Length > 0)
            index++;
    }
}
