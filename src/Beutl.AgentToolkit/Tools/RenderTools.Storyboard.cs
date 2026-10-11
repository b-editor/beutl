using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class RenderTools
{
    private StoryboardRenderPlan PlanStoryboardOutputs(
        Scene scene,
        float renderScale,
        IReadOnlyList<ResolvedStoryboardFrame> resolvedShots,
        string normalizedDirectory,
        string safeBasename,
        bool confirmOverwrite)
    {
        var plannedShots = new List<(ResolvedStoryboardFrame Shot, string ResolvedPath)>(resolvedShots.Count);
        for (int i = 0; i < resolvedShots.Count; i++)
        {
            ResolvedStoryboardFrame shot = resolvedShots[i];
            string stillPath = Path.Combine(
                normalizedDirectory,
                $"{safeBasename}-shot-{i:D2}-{Math.Max(0, (long)Math.Round(shot.Time.TotalMilliseconds)):D8}ms.png");
            string resolvedPath = ToolPaths.ResolveForWrite(stillPath, "outputDirectory");
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            plannedShots.Add((shot, resolvedPath));
        }

        string contactSheetPath = Path.Combine(normalizedDirectory, $"{safeBasename}-contact-sheet.png");
        string resolvedContactSheetPath = ToolPaths.ResolveForWrite(contactSheetPath, "outputDirectory");
        destructiveGuard.EnsureOverwriteAllowed(resolvedContactSheetPath, confirmOverwrite);
        return new StoryboardRenderPlan(scene, renderScale, plannedShots, resolvedContactSheetPath, confirmOverwrite);
    }

    private async Task<RenderStoryboardResponse> RenderStoryboardFramesAsync(
        StoryboardRenderPlan plan,
        RenderJobProgressReporter progress,
        CancellationToken token)
    {
        // Re-verify the overwrite guards at write time: background jobs are serialized, so a
        // preceding job may have created these files after the pre-flight check passed.
        foreach ((_, string plannedPath) in plan.Shots)
        {
            destructiveGuard.EnsureOverwriteAllowed(plannedPath, plan.ConfirmOverwrite);
        }

        destructiveGuard.EnsureOverwriteAllowed(plan.ContactSheetPath, plan.ConfirmOverwrite);

        var renderedShots = new List<RenderStoryboardShot>(plan.Shots.Count);
        var contactSheetFrames = new List<StoryboardContactSheetFrame>(plan.Shots.Count);
        var eyeTraceFrames = new List<StoryboardEyeTraceFrame>(plan.Shots.Count);
        progress.Report(0, plan.Shots.Count, "rendering shots");
        foreach ((ResolvedStoryboardFrame shot, string resolvedPath) in plan.Shots)
        {
            progress.Report(renderedShots.Count, plan.Shots.Count, "rendering shots");
            using RenderedFrameAnalysis frame = await stillRenderer.RenderFrameAnalysisAsync(
                plan.Scene,
                shot.Time,
                plan.RenderScale,
                token).ConfigureAwait(false);
            SaveStoryboardStill(frame.Bitmap, resolvedPath);
            StillFrameVisibilityAnalysis visibility = StillRenderer.AnalyzeFrameVisibility(frame.Bitmap);
            NormalizedFocalPoint? focalPoint = string.Equals(shot.Kind, StoryboardFrameKindShot, StringComparison.Ordinal)
                ? StillRenderer.EstimateFocalPoint(plan.Scene, frame, visibility)
                : null;
            var renderedShot = new RenderStoryboardShot(
                shot.Name,
                shot.Time.TotalSeconds,
                resolvedPath,
                visibility,
                shot.Kind,
                shot.SubdivisionLevel);
            renderedShots.Add(renderedShot);
            contactSheetFrames.Add(ToContactSheetFrame(renderedShot));
            if (focalPoint is not null)
            {
                eyeTraceFrames.Add(new StoryboardEyeTraceFrame(
                    shot.Name,
                    focalPoint));
            }
        }

        progress.Report(plan.Shots.Count, plan.Shots.Count, "contact sheet");
        storyboardRenderer.RenderContactSheet(contactSheetFrames, plan.ContactSheetPath);
        CutEyeTrace[] cutEyeTrace = BuildCutEyeTrace(eyeTraceFrames);
        return new RenderStoryboardResponse(plan.ContactSheetPath, renderedShots, cutEyeTrace);
    }

    private static StoryboardContactSheetFrame ToContactSheetFrame(RenderStoryboardShot shot)
    {
        return new StoryboardContactSheetFrame(
            shot.Name,
            shot.TimeSeconds,
            shot.StillPath,
            shot.Kind,
            shot.SubdivisionLevel);
    }

    private static void SaveStoryboardStill(Bitmap bitmap, string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!bitmap.Save(outputPath, EncodedImageFormat.Png))
        {
            throw new IOException($"Failed to write storyboard still image to '{outputPath}'.");
        }
    }

    private static CutEyeTrace[] BuildCutEyeTrace(IReadOnlyList<StoryboardEyeTraceFrame> frames)
    {
        if (frames.Count < 2)
        {
            return [];
        }

        var result = new CutEyeTrace[frames.Count - 1];
        for (int i = 1; i < frames.Count; i++)
        {
            StoryboardEyeTraceFrame left = frames[i - 1];
            StoryboardEyeTraceFrame right = frames[i];
            double dx = left.FocalPoint.X - right.FocalPoint.X;
            double dy = left.FocalPoint.Y - right.FocalPoint.Y;
            double displacement = Math.Sqrt((dx * dx) + (dy * dy)) / Math.Sqrt(2);
            double roundedDisplacement = Math.Round(displacement, 4, MidpointRounding.AwayFromZero);
            result[i - 1] = new CutEyeTrace(
                left.Name,
                right.Name,
                left.FocalPoint,
                right.FocalPoint,
                roundedDisplacement);
        }

        return result;
    }

    private static IReadOnlyList<ResolvedStoryboardShot> ResolveStoryboardShots(
        Scene scene,
        StoryboardShotInput[]? shots)
    {
        if (shots is { Length: > 0 })
        {
            // Validate explicit shots up front (finite, in-range, non-duplicate) instead of silently
            // filtering/clamping — a typo like a 30s shot in a 5s scene must fail like the
            // timeSeconds path does, not render a blank/misleading frame.
            TimeSpan duration = GetEffectiveSceneDuration(scene);
            var seen = new HashSet<TimeSpan>();
            var explicitShots = new List<ResolvedStoryboardShot>(shots.Length);
            for (int i = 0; i < shots.Length; i++)
            {
                TimeSpan time = ResolveExplicitStoryboardTime(
                    shots[i].TimeSeconds,
                    duration,
                    seen,
                    $"shots[{i}].timeSeconds",
                    "shots");
                explicitShots.Add(new ResolvedStoryboardShot(
                    string.IsNullOrWhiteSpace(shots[i].Name) ? $"shot-{i + 1}" : shots[i].Name.Trim(),
                    time));
            }

            return explicitShots
                .OrderBy(shot => shot.Time)
                .ToArray();
        }

        ResolvedStoryboardShot[] derivedShots = scene.Children
            // Only enabled elements that overlap the visible window can render at their derived time;
            // disabled/off-window draft layers would add blank frames and burn the subdivision cap.
            .Where(element => element.IsEnabled
                && element.Length > TimeSpan.Zero
                && element.Start < scene.Start + scene.Duration
                && element.Start + element.Length > scene.Start)
            .OrderBy(element => element.Start)
            .ThenBy(element => element.ZIndex)
            .Select(element =>
            {
                // Element.Start lives on the absolute timeline axis while shot times are
                // scene-relative (the renderer re-applies scene.Start); normalize and clamp so a
                // trimmed scene (Scene.Start > 0) does not sample past the element.
                TimeSpan midpoint = element.Start + TimeSpan.FromTicks(element.Length.Ticks / 2);
                TimeSpan relative = midpoint - scene.Start;
                if (relative < TimeSpan.Zero)
                {
                    relative = TimeSpan.Zero;
                }

                relative = ClampShotToRenderableRange(relative, scene.Duration);

                return new ResolvedStoryboardShot(
                    string.IsNullOrWhiteSpace(element.Name) ? element.Id.ToString() : element.Name,
                    relative);
            })
            .GroupBy(shot => shot.Time)
            .Select(group => group.First())
            .OrderBy(shot => shot.Time)
            .ToArray();
        if (derivedShots.Length > 0)
        {
            return derivedShots;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "render_storyboard requires explicit shots or at least one timeline Element."));
    }

    internal static IReadOnlyList<ResolvedStoryboardFrame> ResolveStoryboardFrames(
        Scene scene,
        StoryboardShotInput[]? shots,
        int subdivisionLevel)
        => ResolveStoryboardFrames(scene, shots, null, subdivisionLevel);

    internal static IReadOnlyList<ResolvedStoryboardFrame> ResolveStoryboardFrames(
        Scene scene,
        StoryboardShotInput[]? shots,
        double[]? timeSeconds,
        int subdivisionLevel,
        int? frameRate = null)
    {
        IReadOnlyList<ResolvedStoryboardShot> anchors = ResolveExplicitStoryboardTimes(scene, timeSeconds)
                                                        ?? ResolveStoryboardShots(scene, shots);
        int normalizedSubdivisionLevel = NormalizeStoryboardSubdivisionLevel(subdivisionLevel);
        if (normalizedSubdivisionLevel == 0)
        {
            return anchors
                .Select(shot => new ResolvedStoryboardFrame(
                    shot.Name,
                    shot.Time,
                    StoryboardFrameKindShot,
                    0))
                .ToArray();
        }

        TimeSpan dedupeTolerance = GetStoryboardDedupeTolerance(frameRate ?? GetSceneFrameRate(scene));
        ResolvedStoryboardShot[] dedupedAnchors = timeSeconds is null
            ? DeduplicateStoryboardAnchors(anchors, dedupeTolerance)
            : anchors.ToArray();
        if (dedupedAnchors.Length == 0)
        {
            return [];
        }

        int denominator = 1 << normalizedSubdivisionLevel;
        var frames = new List<ResolvedStoryboardFrame>(dedupedAnchors.Length * denominator);
        for (int i = 0; i < dedupedAnchors.Length; i++)
        {
            ResolvedStoryboardShot left = dedupedAnchors[i];
            frames.Add(new ResolvedStoryboardFrame(
                left.Name,
                left.Time,
                StoryboardFrameKindShot,
                0));

            if (i == dedupedAnchors.Length - 1)
            {
                continue;
            }

            ResolvedStoryboardShot right = dedupedAnchors[i + 1];
            TimeSpan gap = right.Time - left.Time;
            if (gap <= dedupeTolerance)
            {
                continue;
            }

            for (int numerator = 1; numerator < denominator; numerator++)
            {
                TimeSpan time = Interpolate(left.Time, right.Time, numerator, denominator);
                if (IsDuplicateStoryboardTime(time, left.Time, dedupeTolerance)
                    || IsDuplicateStoryboardTime(time, right.Time, dedupeTolerance)
                    || (frames.Count > 0 && IsDuplicateStoryboardTime(time, frames[^1].Time, dedupeTolerance)))
                {
                    continue;
                }

                (int reducedNumerator, int reducedDenominator, int frameSubdivisionLevel) =
                    ReduceBinaryFraction(numerator, denominator);
                frames.Add(new ResolvedStoryboardFrame(
                    CreateInbetweenName(
                        left.Name,
                        right.Name,
                        frameSubdivisionLevel,
                        reducedNumerator,
                        reducedDenominator),
                    time,
                    StoryboardFrameKindInbetween,
                    frameSubdivisionLevel));
            }
        }

        return frames;
    }

    private static IReadOnlyList<ResolvedStoryboardShot>? ResolveExplicitStoryboardTimes(
        Scene scene,
        double[]? timeSeconds)
    {
        if (timeSeconds is null)
        {
            return null;
        }

        if (timeSeconds.Length == 0)
        {
            throw CreateStoryboardTimeValidationError("render_storyboard timeSeconds must contain at least one scene time.");
        }

        TimeSpan duration = GetEffectiveSceneDuration(scene);
        var seen = new HashSet<TimeSpan>();
        var result = new List<ResolvedStoryboardShot>(timeSeconds.Length);
        for (int i = 0; i < timeSeconds.Length; i++)
        {
            double seconds = timeSeconds[i];
            TimeSpan time = ResolveExplicitStoryboardTime(
                seconds,
                duration,
                seen,
                $"timeSeconds[{i}]",
                "timeSeconds");
            result.Add(new ResolvedStoryboardShot(CreateExplicitStoryboardTimeName(seconds), time));
        }

        return result
            .OrderBy(shot => shot.Time)
            .ToArray();
    }

    private static TimeSpan ResolveExplicitStoryboardTime(
        double seconds,
        TimeSpan duration,
        HashSet<TimeSpan> seen,
        string itemLabel,
        string listLabel)
    {
        if (!double.IsFinite(seconds))
        {
            throw CreateStoryboardTimeValidationError($"render_storyboard {itemLabel} must be finite.");
        }

        if (seconds < 0 || seconds > duration.TotalSeconds)
        {
            throw CreateStoryboardTimeValidationError(
                $"render_storyboard {itemLabel}={seconds.ToString(CultureInfo.InvariantCulture)} is outside the scene range 0..{duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        TimeSpan time = ClampShotToRenderableRange(TimeSpan.FromSeconds(seconds), duration);
        if (!seen.Add(time))
        {
            throw CreateStoryboardTimeValidationError(
                $"render_storyboard {listLabel} contains duplicate time {seconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        return time;
    }

    // Element.Range treats its end as exclusive, so a shot exactly at Duration renders past every
    // element (a blank frame); pull it one tick inside the scene. Truly out-of-range values are
    // rejected before this by the callers; this only nudges the Duration boundary itself.
    private static TimeSpan ClampShotToRenderableRange(TimeSpan time, TimeSpan duration)
        => duration > TimeSpan.Zero && time >= duration
            ? TimeSpan.FromTicks(duration.Ticks - 1)
            : time;

    private static string CreateExplicitStoryboardTimeName(double seconds)
        => $"t:{seconds.ToString("0.####", CultureInfo.InvariantCulture)}";

    private static ReconcileException CreateStoryboardTimeValidationError(string message)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            message,
            "timeSeconds",
            "Pass finite scene times in seconds within the scene duration, or omit timeSeconds to use explicit shots or auto Element midpoint detection."));
    }

    internal static int NormalizeStoryboardSubdivisionLevel(int subdivisionLevel)
    {
        return Math.Clamp(subdivisionLevel, 0, MaxStoryboardSubdivisionLevel);
    }

    private static void ValidateStoryboardFrameCount(int frameCount, int subdivisionLevel, string target = "subdivisionLevel")
    {
        if (frameCount <= MaxStoryboardFrameCount)
        {
            return;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"render_storyboard would render {frameCount} frames, above the limit of {MaxStoryboardFrameCount}. Lower subdivisionLevel or narrow the shots before rendering.",
            target,
            target == "timeSeconds"
                ? "Lower subdivisionLevel, pass fewer timeSeconds, or split the storyboard review into narrower time ranges."
                : subdivisionLevel > 0
                    ? "Lower subdivisionLevel, pass fewer shots, or split the storyboard review into narrower shot ranges."
                    : "Pass fewer shots or split the storyboard review into narrower shot ranges."));
    }

    private static ResolvedStoryboardShot[] DeduplicateStoryboardAnchors(
        IReadOnlyList<ResolvedStoryboardShot> anchors,
        TimeSpan tolerance)
    {
        var result = new List<ResolvedStoryboardShot>(anchors.Count);
        foreach (ResolvedStoryboardShot anchor in anchors)
        {
            if (result.Count == 0
                || !IsDuplicateStoryboardTime(anchor.Time, result[^1].Time, tolerance))
            {
                result.Add(anchor);
            }
        }

        return [.. result];
    }

    private static TimeSpan GetStoryboardDedupeTolerance(int frameRate)
    {
        return TimeSpan.FromSeconds(0.5d / frameRate);
    }

    internal static int GetSceneFrameRate(Scene scene) => scene.FindHierarchicalParent<Project>().GetFrameRate();

    private static bool IsDuplicateStoryboardTime(TimeSpan left, TimeSpan right, TimeSpan tolerance)
    {
        return (left - right).Duration() <= tolerance;
    }

    private static TimeSpan Interpolate(TimeSpan left, TimeSpan right, int numerator, int denominator)
    {
        long ticks = left.Ticks + (long)Math.Round((right.Ticks - left.Ticks) * (numerator / (double)denominator));
        return TimeSpan.FromTicks(Math.Max(0, ticks));
    }

    private static (int Numerator, int Denominator, int SubdivisionLevel) ReduceBinaryFraction(
        int numerator,
        int denominator)
    {
        while (numerator % 2 == 0 && denominator % 2 == 0)
        {
            numerator /= 2;
            denominator /= 2;
        }

        int level = 0;
        for (int value = denominator; value > 1; value >>= 1)
        {
            level++;
        }

        return (numerator, denominator, level);
    }

    private static string CreateInbetweenName(
        string leftName,
        string rightName,
        int subdivisionLevel,
        int numerator,
        int denominator)
    {
        return $"between:{leftName}~{rightName}@L{subdivisionLevel}:{numerator}/{denominator}";
    }

    private static string NormalizeStoryboardBasename(string basename)
    {
        string normalized = string.IsNullOrWhiteSpace(basename)
            ? "storyboard"
            : Path.GetFileNameWithoutExtension(basename.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "storyboard";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '-');
        }

        return normalized;
    }

    internal sealed record ResolvedStoryboardFrame(
        string Name,
        TimeSpan Time,
        string Kind,
        int SubdivisionLevel);

    internal sealed record ResolvedStoryboardShot(
        string Name,
        TimeSpan Time);

    private sealed record StoryboardEyeTraceFrame(
        string Name,
        NormalizedFocalPoint FocalPoint);

    private sealed record StoryboardRenderPlan(
        Scene Scene,
        float RenderScale,
        IReadOnlyList<(ResolvedStoryboardFrame Shot, string ResolvedPath)> Shots,
        string ContactSheetPath,
        bool ConfirmOverwrite);
}
