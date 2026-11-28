using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Candidate times come from source frames, so trimming and speed move them with the clip.
internal sealed class CutDetectionService(
    HistoryManager history,
    EditorConfig configuration,
    DecoderRegistry? decoders = null)
{
    // Upper bound on decoded frames. A stride keeps long clips bounded, and the minimum interval
    // still separates neighbouring cuts at the resulting sample spacing.
    private const int MaxSamples = 9000;
    private const int HistogramBins = 32;
    private const int MaxSampleWidth = 160;
    private const int MaxSampleHeight = 90;
    private const int RequestedThumbnailHeight = 90;

    internal static bool CanAnalyze(Scene scene, Element element)
        => scene.Children.Contains(element)
           && !scene.IsElementLocked(element)
           && element.Length.Value > MediaTime.Zero
           && FindVideo(element) is not null;

    // Detect only constant mappings. Animated speed or source switches would need a scene-time
    // evaluation on the document thread, which the background decode must not do.
    internal static SourceVideo? FindVideo(Element element)
        => element.Objects.OfType<SourceVideo>().FirstOrDefault(video =>
            video.Source.Value is { HasUri: true } source
            && source.Uri.IsFile
            && video.Speed.Value > 0
            && IsConstant(video.Source)
            && IsConstant(video.Speed)
            && IsConstant(video.OffsetPosition)
            && IsConstant(video.IsLoop));

    private static bool IsConstant(IProperty property)
        => property is not IExpressionProperty { HasExpression: true }
           && property.GetKeyFrameAnimation() is not { KeyFrames.Count: > 0 };

    internal async Task<CutDetectionResult> AnalyzeAsync(Scene scene, Element element, CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VideoCapture capture = Capture(scene, element) ?? throw new InvalidOperationException(Strings.CutDetection_NoVideo);
        return await Task.Run(() => Analyze(capture, cancellationToken, progress), cancellationToken)
            .ConfigureAwait(false);
    }

    // Scene lookups and property reads happen on the calling thread; only decoding reads the file.
    private static VideoCapture? Capture(Scene scene, Element element)
    {
        SourceVideo? video = FindVideo(element);
        if (video?.Source.Value is not { HasUri: true } source || !source.Uri.IsFile)
            return null;

        TimeRange range = element.Range;
        if (range.Duration <= MediaTime.Zero) return null;
        return new VideoCapture(
            source.Uri.LocalPath,
            range,
            video.OffsetPosition.Value,
            video.Speed.Value / 100f,
            video.IsLoop.Value,
            video.GetDeinterlaceOptions(),
            SceneTimeRangeService.GetFrameRate(scene));
    }

    private CutDetectionResult Analyze(VideoCapture capture, CancellationToken cancellationToken, IProgress<int>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DecoderRegistry registry = decoders
            ?? throw new InvalidOperationException(Strings.CutDetection_NoDecoder);
        using MediaReader reader = MediaReader.Open(capture.Path, new MediaOptions(MediaMode.Video)
        {
            IsBackground = true,
            PreferProxy = false,
            Deinterlace = capture.Deinterlace,
            // Decoders that can scale return a smaller frame; others are reduced while sampling.
            VideoThumbnailHeight = RequestedThumbnailHeight,
        }, registry);

        if (!reader.HasVideo) throw new InvalidDataException(Strings.CutDetection_NoFrames);
        VideoStreamInfo info = reader.VideoInfo;
        Rational rate = info.FrameRate;
        if (rate.Numerator <= 0 || rate.Denominator <= 0)
            throw new InvalidDataException(Strings.CutDetection_NoFrames);

        double durationSeconds = info.Duration.ToDouble();
        long frameCount = info.NumFrames > 0 ? info.NumFrames
            : durationSeconds > 0 ? MediaTime.FromSeconds(durationSeconds).ToFrame(rate, MidpointRounding.AwayFromZero)
            : 0;
        if (frameCount <= 0) throw new InvalidDataException(Strings.CutDetection_NoFrames);

        double speed = Math.Max(capture.Speed, 0.0001);
        MediaTime sourceSpan = new((long)Math.Round(capture.ClipRange.Duration.Flicks * speed, MidpointRounding.AwayFromZero));
        long startFrame = capture.Offset.ToFrame(rate, MidpointRounding.AwayFromZero);
        MediaTime endSource = capture.Offset + sourceSpan;
        long endFrame;
        if (capture.Loop)
        {
            endFrame = startFrame + sourceSpan.ToFrame(rate, MidpointRounding.AwayFromZero);
        }
        else
        {
            long firstFrame = Math.Max(0, startFrame);
            long lastFrame = Math.Min(frameCount - 1, endSource.ToFrame(rate, MidpointRounding.AwayFromZero));
            startFrame = firstFrame;
            endFrame = lastFrame;
        }

        long sampleSpan = endFrame - startFrame;
        if (sampleSpan <= 0) return new CutDetectionResult([], capture.ClipRange, capture.SceneFrameRate);
        int stride = (int)Math.Max(1, (sampleSpan + MaxSamples - 2) / (MaxSamples - 1));
        int count = (int)((sampleSpan + stride - 1) / stride) + 1;
        progress?.Report(0);

        var frames = new List<CutFrameScore>(count);
        double[]? previous = null;
        int reported = 0;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long frame = startFrame + (long)i * stride;
            if (capture.Loop) frame = Wrap(frame, frameCount);
            else if (frame >= frameCount) frame = frameCount - 1;

            if (!reader.ReadVideo((int)frame, out Ref<Bitmap>? bitmap))
            {
                if (frames.Count > 0) break;
                throw new InvalidDataException(Strings.CutDetection_NoFrames);
            }

            using (bitmap)
            {
                double[] histogram = ComputeHistogram(bitmap.Value);
                // The sample's scene time advances with source time; for a loop it keeps moving
                // forward even though the decoded frame index wraps.
                MediaTime sceneTime = capture.Loop
                    ? SceneTimeOf(capture, i * stride, rate, speed)
                    : capture.ClipRange.Start + new MediaTime((long)Math.Round(
                        (MediaTime.FromFrames(frame, rate) - capture.Offset).Flicks / speed,
                        MidpointRounding.AwayFromZero));
                double score = previous == null ? 0 : HistogramDistance(previous, histogram);
                frames.Add(new CutFrameScore(sceneTime, score));
                previous = histogram;
            }

            int percent = (int)((long)(i + 1) * 100 / count);
            if (percent > reported)
            {
                reported = percent;
                progress?.Report(percent);
            }
        }

        return new CutDetectionResult(frames, capture.ClipRange, capture.SceneFrameRate);

        static MediaTime SceneTimeOf(VideoCapture capture, long sourceFrameOffset, Rational rate, double speed)
        {
            MediaTime elapsed = MediaTime.FromFrames(sourceFrameOffset, rate);
            return capture.ClipRange.Start + new MediaTime((long)Math.Round(elapsed.Flicks / speed, MidpointRounding.AwayFromZero));
        }
    }

    private static long Wrap(long frame, long count)
    {
        long remainder = frame % count;
        return remainder < 0 ? remainder + count : remainder;
    }

    // A 32-bin luma histogram ignores small motion and chroma noise while still separating
    // solid shots, and its normalized L1 distance is cheap for thousands of frames.
    private static double[] ComputeHistogram(Bitmap bitmap)
    {
        Bitmap? converted = null;
        try
        {
            Bitmap source = bitmap;
            if (bitmap.ColorType != BitmapColorType.Bgra8888)
            {
                converted = bitmap.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
                source = converted;
            }

            int stepX = Math.Max(1, (source.Width + MaxSampleWidth - 1) / MaxSampleWidth);
            int stepY = Math.Max(1, (source.Height + MaxSampleHeight - 1) / MaxSampleHeight);
            var histogram = new double[HistogramBins];
            long samples = 0;
            for (int y = 0; y < source.Height; y += stepY)
            {
                Span<Bgra8888> row = source.GetRow<Bgra8888>(y);
                for (int x = 0; x < row.Length; x += stepX)
                {
                    Bgra8888 pixel = row[x];
                    double luma = (0.0722 * pixel.B + 0.7152 * pixel.G + 0.2126 * pixel.R) / 255d;
                    int bin = Math.Clamp((int)(luma * HistogramBins), 0, HistogramBins - 1);
                    histogram[bin]++;
                    samples++;
                }
            }

            if (samples > 0)
            {
                for (int i = 0; i < histogram.Length; i++) histogram[i] /= samples;
            }

            return histogram;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    // Half the L1 distance maps identical distributions to 0 and disjoint ones to 1.
    private static double HistogramDistance(double[] left, double[] right)
    {
        double distance = 0;
        for (int i = 0; i < left.Length; i++) distance += Math.Abs(left[i] - right[i]);
        return distance * 0.5;
    }

    // The caller owns the document thread. Returns false when there is nothing to cut.
    internal bool ApplySplit(Scene scene, Element element, IReadOnlyList<MediaTime> cuts, bool includeLinked,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveTargets(scene, element, includeLinked, out Element[] originals)) return false;
        if (originals.Any(scene.IsElementLocked)) throw new InvalidOperationException(Strings.CutDetection_LockedLinked);

        MediaTime[] applicable = cuts
            .Where(cut => originals.Any(target => ElementStructureService.LeavesAFrameOnEachSide(scene, target, cut)))
            .Distinct()
            .OrderDescending()
            .ToArray();
        if (applicable.Length == 0) return false;

        bool changed = false;
        history.ExecuteInTransaction(() =>
        {
            foreach (MediaTime cut in applicable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Descending cuts only touch the original left pieces, never the added right pieces.
                Element[] targets = originals.Where(target => target.Start.Value < cut && cut < target.Range.End)
                    .ToArray();
                if (targets.Length == 0) continue;
                var split = ElementStructureService.SplitCore(scene, targets, cut,
                    autoAdjustDuration: configuration.AutoAdjustSceneDuration.Value, decoders: decoders);
                if (split.Count != targets.Length) throw new InvalidOperationException(Strings.CutDetection_ShortSegment);
                changed = true;
            }
        }, CommandNames.AutoSplitByCuts, cancellationToken: cancellationToken);
        return changed;
    }

    internal int AddMarkers(Scene scene, IReadOnlyList<MediaTime> cuts, CancellationToken cancellationToken = default)
    {
        Rational rate = SceneTimeRangeService.GetFrameRate(scene);
        var existing = scene.Markers.Select(marker => marker.Time.Value.RoundToFrame(rate, MidpointRounding.AwayFromZero))
            .ToHashSet();
        MediaTime[] markers = cuts
            .Select(cut => cut.RoundToFrame(rate, MidpointRounding.AwayFromZero))
            .Where(time => existing.Add(time))
            .Distinct()
            .Order()
            .ToArray();
        if (markers.Length == 0) return 0;

        // One history entry for all markers, so one undo removes the whole detection pass.
        history.ExecuteInTransaction(() =>
        {
            foreach (MediaTime time in markers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scene.Markers.Add(new SceneMarker(time, Strings.CutDetection_MarkerName));
            }
        }, CommandNames.AddMarker, cancellationToken: cancellationToken);
        return markers.Length;
    }

    private static bool TryResolveTargets(Scene scene, Element element, bool includeLinked, out Element[] originals)
    {
        originals = [];
        if (!scene.Children.Contains(element) || scene.Uri == null) return false;

        ImmutableHashSet<Guid>? linked = includeLinked ? scene.FindGroup(element.Id.Value) : null;
        originals = linked == null ? [element] : scene.Children.Where(e => linked.Contains(e.Id.Value)).ToArray();
        return true;
    }

    private sealed record VideoCapture(
        string Path,
        TimeRange ClipRange,
        MediaTime Offset,
        double Speed,
        bool Loop,
        VideoDeinterlaceOptions Deinterlace,
        Rational SceneFrameRate);
}

internal sealed class CutDetectionResult(
    IReadOnlyList<CutFrameScore> frames,
    TimeRange clipRange,
    Rational sceneFrameRate)
{
    public IReadOnlyList<CutFrameScore> Frames { get; } = frames;
    public TimeRange ClipRange { get; } = clipRange;
    public Rational SceneFrameRate { get; } = sceneFrameRate;

    // Re-filters the stored scores, so changing a slider never decodes the clip again.
    public IReadOnlyList<MediaTime> GetCuts(double threshold, MediaTime minimumInterval)
        => CutDetector.Detect(Frames, ClipRange, SceneFrameRate, threshold, minimumInterval);
}
