using Beutl.Api.Services;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Services.AI;

// AI request rules that the dialogs, the node executor and the agent tools share.
internal static class AiRequestRules
{
    public static (int Width, int Height, int Horizontal, int Vertical) GetOutpaintDimensions(
        int sourceWidth,
        int sourceHeight,
        int expansionPercent)
    {
        (int expandedWidth, int expandedHeight, int horizontal, int vertical) =
            AiImageEditTasks.GetOutpaintDimensions(sourceWidth, sourceHeight, expansionPercent);
        AiImageDecodeValidator.ValidateDimensions(expandedWidth, expandedHeight);
        return (expandedWidth, expandedHeight, horizontal, vertical);
    }

    public static AiTranscriptionSegment[] ValidateTranscriptionSegments(
        IReadOnlyList<AiTranscriptionSegment> segments,
        double maximumEndSeconds)
    {
        const double TimestampToleranceSeconds = 0.05;
        if (!double.IsFinite(maximumEndSeconds) || maximumEndSeconds <= 0)
            throw new InvalidDataException("The transcription chunk duration is invalid.");
        if (segments.Count == 0)
            return [];
        double previousStart = -1;
        double previousEnd = 0;
        var normalized = new AiTranscriptionSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            AiTranscriptionSegment segment = segments[i];
            if (segment is null
                || !double.IsFinite(segment.Start)
                || !double.IsFinite(segment.End)
                || segment.Start < 0
                || segment.Start < previousStart
                || segment.End < previousEnd
                || segment.Start < previousEnd - TimestampToleranceSeconds
                || segment.End <= segment.Start
                || segment.End > maximumEndSeconds + TimestampToleranceSeconds
                || Math.Min(segment.End, maximumEndSeconds) <= segment.Start
                || string.IsNullOrWhiteSpace(segment.Text))
            {
                // This is a client-side rejection of a potentially paid success, not a
                // settled provider failure. Keep its request key available for recovery.
                throw new InvalidDataException("The transcription provider returned an invalid segment set.");
            }

            normalized[i] = new AiTranscriptionSegment
            {
                Start = segment.Start,
                End = Math.Min(segment.End, maximumEndSeconds),
                Text = segment.Text,
            };
            previousStart = segment.Start;
            previousEnd = normalized[i].End;
        }
        return normalized;
    }
}
