using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Reads the text objects of an AviUtl exedit object file (.exo) as cues. A subtitle cannot hold what the file
/// says about everything else, so the format is only read. The file is Shift-JIS, but nothing here depends on
/// that: every key read is ASCII and the text is hexadecimal UTF-16.
/// </summary>
public sealed partial class ExoCaptionDecoder : ICaptionDecoder
{
    public CaptionImportResult Decode(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Dictionary<string, Dictionary<string, string>> sections = ReadSections(content, out List<string> objects);
        if (!sections.TryGetValue("exedit", out Dictionary<string, string>? header)
            || !TryGetPositive(header, "rate", out long rate)
            || !TryGetPositive(header, "scale", out long scale))
        {
            return CaptionImportResult.Failure(
            [
                new CaptionDiagnostic(CaptionDiagnosticKinds.InvalidHeader, "The file has no [exedit] section with a rate and a scale."),
            ]);
        }

        var cues = new List<CaptionCue>();
        var diagnostics = new List<CaptionDiagnostic>();
        CaptionCue? previous = null;
        foreach (string name in objects)
        {
            Dictionary<string, string> item = sections[name];
            bool chained = item.GetValueOrDefault("chain") == "1";
            // A section the object is split into at a mid point. The text stays that of the first one.
            if (chained && previous != null)
            {
                if (TryReadTimes(item, rate, scale, out _, out TimeSpan end))
                {
                    CaptionCue extended = previous with { End = end };
                    cues[^1] = extended;
                    previous = extended;
                }

                continue;
            }

            previous = null;
            if (!sections.TryGetValue(name + ".0", out Dictionary<string, string>? first)
                || !first.ContainsKey("font")
                || !first.TryGetValue("text", out string? hex))
            {
                continue;
            }

            if (!TryReadText(hex, out string text))
            {
                diagnostics.Add(new CaptionDiagnostic(CaptionDiagnosticKinds.InvalidStructure, $"Object {name} holds text that is not hexadecimal UTF-16."));
                continue;
            }

            string plain = ControlTags().Replace(text, string.Empty);
            if (string.IsNullOrWhiteSpace(plain)) continue;
            if (plain.Length != text.Length)
            {
                diagnostics.Add(new CaptionDiagnostic(CaptionDiagnosticKinds.UnsupportedMarkup, $"Object {name} holds control tags, which were left out."));
            }

            if (!TryReadTimes(item, rate, scale, out TimeSpan start, out TimeSpan stop))
            {
                diagnostics.Add(new CaptionDiagnostic(CaptionDiagnosticKinds.InvalidTiming, $"Object {name} has no valid start and end frames."));
                continue;
            }

            previous = new CaptionCue(start, stop, plain);
            cues.Add(previous);
        }

        return cues.Count > 0 || diagnostics.Count == 0
            ? CaptionImportResult.Imported(new CaptionDocument(cues.OrderBy(cue => cue.Start)), diagnostics)
            : CaptionImportResult.Failure(diagnostics);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadSections(string content, out List<string> objects)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        objects = [];
        Dictionary<string, string>? current = null;
        foreach (string line in CaptionTextUtilities.GetLines(content))
        {
            if (line is ['[', .. var name, ']'])
            {
                current = [];
                sections[name] = current;
                if (name.All(char.IsAsciiDigit)) objects.Add(name);
            }
            else if (current != null && line.IndexOf('=') is > 0 and var separator)
            {
                current[line[..separator]] = line[(separator + 1)..];
            }
        }

        return sections;
    }

    private static bool TryGetPositive(Dictionary<string, string> section, string key, out long value) =>
        long.TryParse(section.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    // Frames count from 1 and the end frame is shown, so an object on frames 1 to 30 at 30 fps lasts the first second.
    private static bool TryReadTimes(Dictionary<string, string> item, long rate, long scale, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        if (!TryGetPositive(item, "start", out long first) || !TryGetPositive(item, "end", out long last) || last < first)
            return false;

        Int128 startTicks = (Int128)(first - 1) * scale * TimeSpan.TicksPerSecond / rate;
        Int128 endTicks = (Int128)last * scale * TimeSpan.TicksPerSecond / rate;
        if (endTicks > TimeSpan.MaxValue.Ticks) return false;

        start = TimeSpan.FromTicks((long)startTicks);
        end = TimeSpan.FromTicks((long)endTicks);
        return true;
    }

    // Hexadecimal UTF-16LE, filled with zeros up to a fixed length.
    private static bool TryReadText(string hex, out string text)
    {
        text = string.Empty;
        try
        {
            string decoded = Encoding.Unicode.GetString(Convert.FromHexString(hex));
            int end = decoded.IndexOf('\0');
            text = CaptionTextUtilities.NormalizeLineEndings(end >= 0 ? decoded[..end] : decoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // The control text of exedit: size and font, color, position, speed, and a script in <? ?>.
    [GeneratedRegex(@"<(?:s[0-9.]*(?:,[^<>]*)?|#[0-9a-fA-F]*(?:,[0-9a-fA-F]*)?|p[-+0-9.,]*|r[0-9.]*|w\*?[0-9.]*|\?.*?\?)>", RegexOptions.Singleline)]
    private static partial Regex ControlTags();
}
