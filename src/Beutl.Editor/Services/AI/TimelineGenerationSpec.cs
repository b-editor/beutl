using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Services.AI;

/// <summary>What a generation started from the timeline makes.</summary>
public enum TimelineGenerationKind
{
    /// <summary>A new clip, from a prompt and optionally a first and last frame.</summary>
    Video,

    /// <summary>A continuation of a clip; the service returns the clip with the extension added.</summary>
    VideoExtend,

    /// <summary>A clip remade from another one.</summary>
    VideoEdit,

    /// <summary>A picture remade from another one.</summary>
    ImageEdit,
}

/// <summary>
/// What a timeline generation asks for, apart from the pictures and clips it is given.
/// It is saved with the element it makes, so the element can be generated again.
/// </summary>
public sealed record TimelineGenerationSpec
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public TimelineGenerationKind Kind { get; init; }

    public string Prompt { get; init; } = string.Empty;

    /// <summary>The model, or null for the service's default.</summary>
    public string? ModelId { get; init; }

    public string? Resolution { get; init; }

    public string? AspectRatio { get; init; }

    /// <summary>The clip's length, or for an extension the length added.</summary>
    public int DurationSeconds { get; init; } = GenerativeVideoCapabilities.DefaultDuration;

    public bool GenerateAudio { get; init; }

    public AiImageEditTask ImageTask { get; init; }

    public int OutpaintExpansionPercent { get; init; } = 25;

    /// <summary>The operation whose models and limits apply.</summary>
    [JsonIgnore]
    public string OperationId => OperationIdOf(Kind, ImageTask);

    [JsonIgnore]
    public bool RequiresPrompt => Kind switch
    {
        TimelineGenerationKind.ImageEdit => ImageTask.RequiresPrompt(),
        _ => true,
    };

    public static string OperationIdOf(TimelineGenerationKind kind, AiImageEditTask task) => kind switch
    {
        TimelineGenerationKind.Video => "video.generate",
        TimelineGenerationKind.VideoExtend => "video.extend",
        TimelineGenerationKind.VideoEdit => "video.edit",
        TimelineGenerationKind.ImageEdit => $"image.edit.{task.ToId()}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Identifies the scalar inputs: two specs that would send the same request apart from
    /// their pictures and clips have the same fingerprint.
    /// </summary>
    public string ComputeParameterFingerprint()
    {
        bool video = Kind is TimelineGenerationKind.Video;
        bool timed = Kind is TimelineGenerationKind.Video or TimelineGenerationKind.VideoExtend;
        bool image = Kind is TimelineGenerationKind.ImageEdit;
        return GenerativeFingerprint.Combine(
        [
            Kind.ToString(),
            RequiresPrompt ? Prompt.Trim() : null,
            ModelId,
            video ? Resolution : null,
            video ? AspectRatio : null,
            timed ? DurationSeconds.ToString(CultureInfo.InvariantCulture) : null,
            video && GenerateAudio ? "audio" : null,
            image ? ImageTask.ToString() : null,
            image && ImageTask == AiImageEditTask.Outpaint
                ? OutpaintExpansionPercent.ToString(CultureInfo.InvariantCulture)
                : null,
        ]);
    }

    public string ToJson() => JsonSerializer.Serialize(this, s_jsonOptions);

    /// <summary>Reads a spec saved with an element, or null when it cannot be read.</summary>
    public static TimelineGenerationSpec? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TimelineGenerationSpec>(json, s_jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
