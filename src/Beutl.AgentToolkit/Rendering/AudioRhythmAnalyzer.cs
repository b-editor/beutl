using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Media.Decoding;
using EditorAnalyzer = Beutl.Editor.Services.AudioRhythmAnalyzer;
using EditorError = Beutl.Editor.Services.AudioRhythmError;
using EditorException = Beutl.Editor.Services.AudioRhythmException;

namespace Beutl.AgentToolkit.Rendering;

public sealed record AudioRhythmWindow(
    double StartSeconds,
    double DurationSeconds);

public sealed record AudioRhythmAnalysis(
    int SampleRate,
    AudioRhythmWindow AnalyzedWindow,
    double EstimatedBpm,
    double Confidence,
    IReadOnlyList<double> BeatTimesSeconds,
    IReadOnlyList<double> StrongOnsetTimesSeconds);

// The estimator lives in Beutl.Editor so the timeline can use it. This keeps the toolkit's public shape and
// reports the estimator's failures as the tool errors its callers already expect.
public sealed class AudioRhythmAnalyzer(DecoderRegistry decoders)
{
    private readonly EditorAnalyzer _analyzer = new(decoders);

    public async ValueTask<AudioRhythmAnalysis> AnalyzeFileAsync(
        string path,
        double? startSeconds,
        double? durationSeconds,
        double? expectedBpmMin,
        double? expectedBpmMax,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _analyzer.AnalyzeFileAsync(
                path,
                startSeconds,
                durationSeconds,
                expectedBpmMin,
                expectedBpmMax,
                cancellationToken).ConfigureAwait(false);
            return new AudioRhythmAnalysis(
                result.SampleRate,
                new AudioRhythmWindow(result.AnalyzedWindow.StartSeconds, result.AnalyzedWindow.DurationSeconds),
                result.EstimatedBpm,
                result.Confidence,
                result.BeatTimesSeconds,
                result.StrongOnsetTimesSeconds);
        }
        catch (EditorException ex)
        {
            throw MapException(ex);
        }
    }

    private static Exception MapException(EditorException exception)
    {
        return exception.Kind switch
        {
            EditorError.MediaNotFound => new ReconcileException(
                new ToolError(ErrorCode.MediaNotFound, exception.Message, exception.Target)),
            EditorError.ValidationRejected => new ReconcileException(
                new ToolError(ErrorCode.ValidationRejected, exception.Message, exception.Target, exception.Hint)),
            _ => new CodecUnavailableException(exception.Message, exception),
        };
    }
}
