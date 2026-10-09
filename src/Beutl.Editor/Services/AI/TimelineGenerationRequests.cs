using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Services.AI;

/// <summary>The pictures and clips a timeline generation is given, as files.</summary>
public sealed record TimelineGenerationInputs
{
    /// <summary>The picture edited, or the frame a generated clip starts on.</summary>
    public string? ImagePath { get; init; }

    /// <summary>The frame a generated clip ends on.</summary>
    public string? LastFramePath { get; init; }

    /// <summary>The clip edited or extended.</summary>
    public string? VideoPath { get; init; }
}

/// <summary>Turns what the timeline asks for into the requests the node graph's executor runs.</summary>
public static class TimelineGenerationRequests
{
    public static GenerativeRequest Build(
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs,
        string requestKeySeed)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrEmpty(requestKeySeed);

        string prompt = spec.Prompt.Trim();
        if (spec.RequiresPrompt && prompt.Length == 0)
            throw new GenerativeExecutionException(Strings.AiPromptRequired);

        string? model = string.IsNullOrWhiteSpace(spec.ModelId) ? null : spec.ModelId.Trim();
        string parameters = spec.ComputeParameterFingerprint();
        switch (spec.Kind)
        {
            case TimelineGenerationKind.Video:
                GenerativeImageInput? first = inputs.ImagePath is { } firstPath ? ReadImage(firstPath, "first-frame") : null;
                GenerativeImageInput? last = inputs.LastFramePath is { } lastPath ? ReadImage(lastPath, "last-frame") : null;
                if (last is not null && first is null)
                    throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
                return new AiVideoGenerationNodeRequest(spec.OperationId)
                {
                    Prompt = prompt,
                    DurationSeconds = spec.DurationSeconds,
                    Resolution = string.IsNullOrWhiteSpace(spec.Resolution)
                        ? GenerativeVideoCapabilities.DefaultResolutions[0]
                        : spec.Resolution.Trim(),
                    AspectRatio = string.IsNullOrWhiteSpace(spec.AspectRatio)
                        ? GenerativeVideoCapabilities.DefaultAspectRatios[0]
                        : spec.AspectRatio.Trim(),
                    GenerateAudio = spec.GenerateAudio,
                    FirstFrame = first,
                    LastFrame = last,
                    ModelId = model,
                    RequestKeySeed = requestKeySeed,
                    ParameterFingerprint = parameters,
                };

            case TimelineGenerationKind.VideoExtend:
            case TimelineGenerationKind.VideoEdit:
                string video = inputs.VideoPath
                    ?? throw new GenerativeExecutionException(Strings.AiVideoNoSourceSelected);
                return new AiVideoEditNodeRequest(spec.OperationId)
                {
                    Mode = spec.Kind == TimelineGenerationKind.VideoExtend ? AiVideoEditMode.Extend : AiVideoEditMode.Edit,
                    Prompt = prompt,
                    DurationSeconds = spec.Kind == TimelineGenerationKind.VideoExtend ? spec.DurationSeconds : 0,
                    SourceVideo = GenerativeInputs.ReadVideoFile(video, "source"),
                    ModelId = model,
                    RequestKeySeed = requestKeySeed,
                    ParameterFingerprint = parameters,
                };

            case TimelineGenerationKind.ImageEdit:
                string image = inputs.ImagePath
                    ?? throw new GenerativeExecutionException(Strings.AiEditNoSourceSelected);
                return new AiImageEditNodeRequest(spec.OperationId)
                {
                    Task = spec.ImageTask,
                    Prompt = spec.RequiresPrompt ? prompt : null,
                    OutpaintExpansionPercent = spec.ImageTask == AiImageEditTask.Outpaint
                        ? spec.OutpaintExpansionPercent
                        : null,
                    Image = ReadImage(image, "source"),
                    ModelId = model,
                    RequestKeySeed = requestKeySeed,
                    ParameterFingerprint = parameters,
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(spec));
        }
    }

    /// <summary>Reads a picture and encodes it as PNG, the form every request sends pictures in.</summary>
    public static GenerativeImageInput ReadImage(string path, string name)
    {
        try
        {
            using Bitmap bitmap = Bitmap.FromFile(path);
            using var stream = new MemoryStream();
            if (!bitmap.Save(stream, EncodedImageFormat.Png))
                throw new GenerativeExecutionException(Strings.AiEditSourcePreviewFailed);
            return new GenerativeImageInput($"{name}.png", stream.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new GenerativeExecutionException(Strings.AiEditSourcePreviewFailed, ex);
        }
    }
}
