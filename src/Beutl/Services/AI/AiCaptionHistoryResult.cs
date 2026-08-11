using System.Text.Json;
using Beutl.Api.Services;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal sealed record AiCaptionHistoryResult(
    AiJobId JobId,
    AiTranscriptionSegment[] Segments,
    string? Language,
    GenerationProvenance Provenance);

internal static class AiCaptionHistoryResultParser
{
    public const int MaximumResultBytes = 8 * 1024 * 1024;
    private const int MaximumSegmentCount = 10_000;
    private const int MaximumTextLength = 100_000;

    public static bool TryParse(
        ReadOnlySpan<byte> bytes,
        string expectedKind,
        AiJobId jobId,
        DateTimeOffset generatedAt,
        out AiCaptionHistoryResult? result)
    {
        result = null;
        if (bytes.IsEmpty || bytes.Length > MaximumResultBytes)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 16,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetInt32(root, "version", out int version)
                || version != 1
                || !TryGetString(root, "kind", out string? kind)
                || !string.Equals(kind, expectedKind, StringComparison.Ordinal)
                || !root.TryGetProperty("segments", out JsonElement segments)
                || segments.ValueKind != JsonValueKind.Array
                || segments.GetArrayLength() is <= 0 or > MaximumSegmentCount)
            {
                return false;
            }

            return kind switch
            {
                "stt" => TryParseTranscription(root, segments, jobId, generatedAt, out result),
                "translation" => TryParseTranslation(root, segments, jobId, generatedAt, out result),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseTranscription(
        JsonElement root,
        JsonElement segments,
        AiJobId jobId,
        DateTimeOffset generatedAt,
        out AiCaptionHistoryResult? result)
    {
        result = null;
        string? language = TryGetOptionalString(root, "language");
        var parsed = new List<AiTranscriptionSegment>(segments.GetArrayLength());
        foreach (JsonElement segment in segments.EnumerateArray())
        {
            if (!TryGetTimeRange(segment, out double start, out double end)
                || !TryGetString(segment, "text", out string? text)
                || string.IsNullOrWhiteSpace(text)
                || text.Length > MaximumTextLength)
            {
                return false;
            }
            parsed.Add(new AiTranscriptionSegment
            {
                Start = start,
                End = end,
                Text = text,
            });
        }

        double duration = parsed.Max(segment => segment.End)
            - parsed.Min(segment => segment.Start);
        result = new AiCaptionHistoryResult(
            jobId,
            parsed.ToArray(),
            language,
            AiProvenanceFactory.Transcription(
                "job_history",
                TimeSpan.FromSeconds(duration),
                language,
                1,
                generatedAt));
        return true;
    }

    private static bool TryParseTranslation(
        JsonElement root,
        JsonElement segments,
        AiJobId jobId,
        DateTimeOffset generatedAt,
        out AiCaptionHistoryResult? result)
    {
        result = null;
        string? targetLanguage = TryGetOptionalString(root, "targetLanguage");
        if (targetLanguage is null)
        {
            return false;
        }
        string? sourceLanguage = TryGetOptionalString(root, "sourceLanguage");
        var groups = new Dictionary<string, TranslationGroup>(StringComparer.Ordinal);
        int sequence = 0;
        foreach (JsonElement segment in segments.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object
                || !TryGetString(segment, "text", out string? text)
                || string.IsNullOrWhiteSpace(text)
                || text.Length > MaximumTextLength
                || !segment.TryGetProperty("context", out JsonElement context)
                || context.ValueKind != JsonValueKind.Object
                || !TryGetString(context, "groupId", out string? groupId)
                || string.IsNullOrWhiteSpace(groupId)
                || groupId.Length > 64
                || !TryGetInt32(context, "partIndex", out int partIndex)
                || partIndex is < 0 or >= MaximumSegmentCount
                || !TryGetTimeRange(context, out double start, out double end))
            {
                return false;
            }

            if (!groups.TryGetValue(groupId, out TranslationGroup? group))
            {
                group = new TranslationGroup(sequence++, start, end);
                groups.Add(groupId, group);
            }
            else if (group.Start != start || group.End != end)
            {
                return false;
            }
            if (!group.Parts.TryAdd(partIndex, text))
                return false;
        }

        var parsed = new List<AiTranscriptionSegment>(groups.Count);
        foreach (TranslationGroup group in groups.Values
                     .OrderBy(group => group.Start)
                     .ThenBy(group => group.Sequence))
        {
            int expectedPart = group.Parts.Keys.First();
            var text = new System.Text.StringBuilder();
            foreach ((int partIndex, string part) in group.Parts)
            {
                if (partIndex != expectedPart++)
                    return false;
                text.Append(part);
            }
            parsed.Add(new AiTranscriptionSegment
            {
                Start = group.Start,
                End = group.End,
                Text = text.ToString(),
            });
        }

        result = new AiCaptionHistoryResult(
            jobId,
            parsed.ToArray(),
            targetLanguage,
            AiProvenanceFactory.Translation(
                sourceLanguage,
                targetLanguage,
                1,
                generatedAt));
        return true;
    }

    private static bool TryGetTimeRange(
        JsonElement element,
        out double start,
        out double end)
    {
        start = default;
        end = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("start", out JsonElement startElement)
            && startElement.TryGetDouble(out start)
            && double.IsFinite(start)
            && start >= 0
            && element.TryGetProperty("end", out JsonElement endElement)
            && endElement.TryGetDouble(out end)
            && double.IsFinite(end)
            && end > start;
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        return element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString()) is not null;
    }

    private static string? TryGetOptionalString(JsonElement element, string name)
        => TryGetString(element, name, out string? value)
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= 32
                ? value.Trim().ToLowerInvariant()
                : null;

    private sealed class TranslationGroup(int sequence, double start, double end)
    {
        public int Sequence { get; } = sequence;

        public double Start { get; } = start;

        public double End { get; } = end;

        public SortedDictionary<int, string> Parts { get; } = [];
    }
}

internal sealed class SizeLimitedMemoryStream(int maximumBytes) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureWithinLimit(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureWithinLimit(buffer.Length);
        base.Write(buffer);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureWithinLimit(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureWithinLimit(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        EnsureWithinLimit(1);
        base.WriteByte(value);
    }

    private void EnsureWithinLimit(int additionalBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (additionalBytes < 0 || Position > maximumBytes - additionalBytes)
            throw new InvalidDataException("The AI caption result exceeds the supported size.");
    }
}
