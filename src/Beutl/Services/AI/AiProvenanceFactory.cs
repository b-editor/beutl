using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal static class AiProvenanceFactory
{
    private const string ProducerId = "beutl.ai";

    public static GenerationProvenance ImageGeneration(string size, DateTimeOffset generatedAt)
        => Create(
            "image.generate",
            generatedAt,
            Parameters(("size", size)));

    public static GenerationProvenance ImageEdit(
        string task,
        string? sourceElementId,
        int? expansionPercent,
        DateTimeOffset generatedAt)
    {
        var parameters = Parameters(("task", task));
        if (expansionPercent is { } expansion)
        {
            parameters = parameters.Add(
                "expansionPercent",
                expansion.ToString(CultureInfo.InvariantCulture));
        }

        return Create(
            $"image.edit.{task.Replace('_', '.')}",
            generatedAt,
            parameters,
            SourceElements(("sourceImage", sourceElementId)));
    }

    public static GenerationProvenance VideoGeneration(
        int durationSeconds,
        string resolution,
        bool hasFirstFrame,
        bool hasLastFrame,
        string? firstFrameElementId,
        string? lastFrameElementId,
        DateTimeOffset generatedAt)
        => Create(
            "video.generate",
            generatedAt,
            Parameters(
                ("durationSeconds", durationSeconds.ToString(CultureInfo.InvariantCulture)),
                ("resolution", resolution),
                ("hasFirstFrame", hasFirstFrame ? "true" : "false"),
                ("hasLastFrame", hasLastFrame ? "true" : "false")),
            SourceElements(
                ("firstFrame", firstFrameElementId),
                ("lastFrame", lastFrameElementId)));

    public static GenerationProvenance Transcription(
        string sourceKind,
        TimeSpan duration,
        string? language,
        int chunkCount,
        DateTimeOffset generatedAt)
        => Create(
            "audio.transcribe",
            generatedAt,
            Parameters(
                ("source", sourceKind),
                ("durationSeconds", Math.Ceiling(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
                ("language", language ?? "auto"),
                ("chunkCount", chunkCount.ToString(CultureInfo.InvariantCulture))));

    public static GenerationProvenance Translation(
        string? sourceLanguage,
        string targetLanguage,
        int batchCount,
        DateTimeOffset generatedAt)
        => Create(
            "subtitle.translate",
            generatedAt,
            Parameters(
                ("sourceLanguage", sourceLanguage ?? "auto"),
                ("targetLanguage", targetLanguage),
                ("batchCount", batchCount.ToString(CultureInfo.InvariantCulture))));

    public static GenerationProvenance ImportedHistoryResult(
        string operation,
        string? imageSize,
        int? durationSeconds,
        string? resolution,
        string? task,
        DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var parameters = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        AddIfPresent(parameters, "size", imageSize);
        AddIfPresent(
            parameters,
            "durationSeconds",
            durationSeconds?.ToString(CultureInfo.InvariantCulture));
        AddIfPresent(parameters, "resolution", resolution);
        AddIfPresent(parameters, "task", task);
        return Create(operation, generatedAt, parameters.ToImmutable());
    }

    private static GenerationProvenance Create(
        string operation,
        DateTimeOffset generatedAt,
        ImmutableDictionary<string, string> parameters,
        ImmutableDictionary<string, string>? sourceElements = null)
    {
        var payload = new AiGenerationPayload(
            parameters,
            sourceElements ?? ImmutableDictionary<string, string>.Empty);
        return new GenerationProvenance(
            ProducerId,
            operation,
            1,
            JsonSerializer.SerializeToElement(payload),
            generatedAt);
    }

    private static ImmutableDictionary<string, string> Parameters(
        params (string Key, string Value)[] values)
        => values.ToImmutableDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);

    private static ImmutableDictionary<string, string> SourceElements(
        params (string Key, string? Value)[] values)
        => values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToImmutableDictionary(
                item => item.Key,
                item => item.Value!,
                StringComparer.Ordinal);

    private static void AddIfPresent(
        ImmutableDictionary<string, string>.Builder values,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(key, value);
        }
    }

    private sealed record AiGenerationPayload(
        [property: JsonPropertyName("parameters")]
        ImmutableDictionary<string, string> Parameters,
        [property: JsonPropertyName("sourceElements")]
        ImmutableDictionary<string, string> SourceElements);
}
