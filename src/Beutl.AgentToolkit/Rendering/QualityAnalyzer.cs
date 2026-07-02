using Beutl.AgentToolkit.Design;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record QualityReviewResponse(
    bool PassesQualityGate,
    string Verdict,
    IReadOnlyList<QualityIssue> Issues,
    QualityMetrics Metrics,
    IReadOnlyList<string> ReviewNotes);

public sealed record QualityIssue(
    string Category,
    string Severity,
    string Message,
    string Evidence,
    string SuggestedFix,
    string? Time,
    IReadOnlyList<string> ElementIds,
    IReadOnlyList<string> ObjectIds);

public sealed record QualityMetrics(
    TypographyMetrics Typography,
    ShapeDiversityMetrics ShapeDiversity,
    PaletteMetrics Palette,
    StructureMetrics Structure,
    TempoMetrics Tempo,
    BackgroundRichnessMetrics BackgroundRichness,
    LayerDensityMetrics LayerDensity,
    MotionContinuityMetrics MotionContinuity);

public sealed record TypographyMetrics(
    int TextObjectCount,
    int AllCapsTextCount,
    int ExcessiveSpacingCount,
    int TextPlateMismatchCount);

public sealed record ShapeDiversityMetrics(
    int RectShapeCount,
    int BackgroundRectShapeCount,
    int NonBackgroundRectShapeCount,
    int RoundedRectShapeCount,
    int EllipseShapeCount,
    double RectDominanceRatio,
    int AmbiguousDecorativeShapeCount);

public sealed record PaletteMetrics(
    int ColorCount,
    double AverageSaturation,
    double MaxSaturation,
    double LumaRange,
    bool HasDarkTealCyanMagentaPalette,
    bool HasOversaturatedPalette,
    bool HasLowContrastPalette,
    int HardGradientObjectCount,
    int HardGradientTransitionCount,
    double HarmonyScore,
    string HarmonyScheme,
    double HueRelationshipScore,
    double SaturationBalanceScore,
    double LumaBalanceScore,
    bool HasLowHarmonyScore);

public sealed record BackgroundRichnessMetrics(
    int FullFrameBackgroundLayerCount,
    int FlatSingleLayerBackgroundCount,
    int RichBackgroundLayerCount);

public sealed record StructureMetrics(
    int ElementCount,
    int MultiObjectElementCount,
    int NonFlowMultiObjectElementCount,
    int FlowMultiObjectElementCount,
    int UnclearForegroundShapeCount,
    int AnimatedShapeWithoutMotionIntentCount);

public sealed record TempoMetrics(
    bool HighTempoProfile,
    double TargetBpm,
    double BeatDurationSeconds,
    double RequiredTimelineEventsPerSecond,
    double RequiredTotalEventsPerSecond,
    int TimelineEventCount,
    double TimelineEventsPerSecond,
    int KeyFrameEventCount,
    double KeyFrameEventsPerSecond,
    int LongForegroundGapCount,
    double LongestForegroundEventGapSeconds,
    int SlowHoldCount,
    double LongestForegroundHoldSeconds);

public sealed record LayerDensityMetrics(
    bool MotionGraphicsIntent,
    int TimeBandCount,
    double TimeBandSeconds,
    double AverageVisibleLayerCount,
    int MinimumVisibleLayerCount,
    int MaximumVisibleLayerCount,
    double AverageForegroundLayerCount,
    int MinimumForegroundLayerCount,
    int MaximumForegroundLayerCount,
    int BandsWithBackground,
    int BandsWithMidground,
    int BandsWithForeground,
    int BandsWithAllDepthBands,
    int BandsBelowHalfPlannedForegroundLayerCount,
    double PlannedForegroundElementsPerShot,
    double PlanHalfFloor,
    bool DensityPlanViolation,
    IReadOnlyList<LayerDensityBandMetrics> Bands);

public sealed record LayerDensityBandMetrics(
    double StartSeconds,
    double EndSeconds,
    int VisibleLayerCount,
    int ForegroundLayerCount,
    bool HasBackground,
    bool HasMidground,
    bool HasForeground);

public sealed record MotionContinuityMetrics(
    bool MotionEvaluated,
    string? MotionVerdict,
    double MinimumChangedPixelRatio,
    double AverageChangedPixelRatio,
    int HardCutLikeBoundaryCount,
    int ShortSegmentCount,
    int AnimatedPropertyCount);

public sealed class QualityAnalyzer(MotionVariationAnalyzer motionVariationAnalyzer)
{
    private const string Critical = "critical";
    private const string Major = "major";
    private const string Minor = "minor";

    // Advisory issues never fail the quality gate; only Critical/Major do. Aesthetic
    // opinions use this so they surface as guidance without blocking export.
    private const string Advisory = Minor;

    // Gate policy: typographyReadTime, elementStructure, and motionContinuity are
    // the standing deterministic blockers. layerDensity can also block only when a
    // motion-graphics caller supplies a quantitative plan and authored foreground
    // density falls below half of that plan; palette/background aesthetics remain advisory.

    // A deviation the brief explicitly opted into is guidance, not an accident: Advisory
    // instead of a gate-failing Major. An unsignalled deviation still blocks.
    private static string IntentSeverity(bool intentPresent) => intentPresent ? Advisory : Major;

    public async ValueTask<QualityReviewResponse> AnalyzeAsync(
        Scene scene,
        IReadOnlyList<TimeSpan>? timeSeconds,
        int sampleCount,
        float renderScale,
        string? styleProfile,
        bool allowAllCaps,
        bool allowHardCuts,
        bool allowRectDominance,
        bool relaxAesthetics,
        bool allowStillness,
        bool allowDenseText,
        bool allowMultiObjectElements,
        bool allowMonochrome,
        bool allowMinimalDensity,
        double plannedForegroundElementsPerShot,
        bool evaluateMotion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // relaxAesthetics only suppresses non-blocking aesthetic/pacing advisories; the
        // blocking checks (read time, element structure, motion) still run regardless.
        bool relaxRectDominance = allowRectDominance || relaxAesthetics;
        bool relaxHardCuts = allowHardCuts || relaxAesthetics;

        SceneObjectInfo[] objects = EnumerateObjects(scene).ToArray();
        List<QualityIssue> issues = [];
        TypographyMetrics typography = AnalyzeTypography(objects, allowAllCaps, issues);
        ShapeDiversityMetrics shapeDiversity = AnalyzeShapeDiversity(scene, objects, relaxRectDominance, relaxAesthetics, issues);
        StructureMetrics structure = AnalyzeStructure(scene, objects, allowMultiObjectElements, issues);
        int textPlateMismatchCount = AnalyzeTextBackgroundFit(scene, objects, issues);
        typography = typography with { TextPlateMismatchCount = textPlateMismatchCount };
        PaletteMetrics palette = AnalyzePalette(scene, objects, allowMonochrome, issues);
        BackgroundRichnessMetrics backgroundRichness = AnalyzeBackgroundRichness(scene, objects, issues);
        AnalyzeMaterialUiLook(objects, relaxAesthetics, issues);
        AnalyzeDesignStructure(scene, objects, styleProfile, allowDenseText, issues);
        TempoMetrics tempo = AnalyzeTempo(scene, objects, styleProfile, relaxAesthetics, issues);
        LayerDensityMetrics layerDensity = AnalyzeLayerDensity(
            scene,
            objects,
            styleProfile,
            allowMinimalDensity,
            plannedForegroundElementsPerShot,
            issues);
        MotionContinuityMetrics motion = await AnalyzeMotionAsync(
            scene,
            objects,
            timeSeconds,
            sampleCount,
            renderScale,
            relaxHardCuts,
            relaxAesthetics,
            allowStillness,
            evaluateMotion,
            issues,
            cancellationToken).ConfigureAwait(false);

        bool hasBlockingIssue = issues.Any(issue => issue.Severity is Critical or Major);
        string verdict = hasBlockingIssue
            ? "quality-issues-found"
            : string.Equals(styleProfile, "draft", StringComparison.OrdinalIgnoreCase)
                ? "quality-ok-draft"
                : "quality-ok";

        List<string> notes =
        [
            hasBlockingIssue
                ? "Resolve all critical and major quality issues before exporting a final preview."
                : "No critical or major deterministic quality issues were found.",
            "This review uses deterministic document, color, geometry, and rendered-motion heuristics; it does not use OCR or generative image judging."
        ];

        if (!string.IsNullOrWhiteSpace(styleProfile))
        {
            notes.Add($"Style profile: {styleProfile.Trim()}.");
        }

        return new QualityReviewResponse(
            !hasBlockingIssue,
            verdict,
            issues,
            new QualityMetrics(typography, shapeDiversity, palette, structure, tempo, backgroundRichness, layerDensity, motion),
            notes);
    }

    private static TypographyMetrics AnalyzeTypography(
        IReadOnlyList<SceneObjectInfo> objects,
        bool allowAllCaps,
        List<QualityIssue> issues)
    {
        int textCount = 0;
        int allCapsCount = 0;
        int spacingCount = 0;
        foreach (SceneObjectInfo info in objects)
        {
            if (info.Object is not TextBlock textBlock)
            {
                continue;
            }

            textCount++;
            string text = textBlock.Text.CurrentValue ?? string.Empty;
            if (LooksLikeLongAllCaps(text))
            {
                allCapsCount++;
                issues.Add(CreateIssue(
                    "typography",
                    Advisory,
                    "Long all-caps text tends to make generated edits look generic and rigid.",
                    $"Text '{Shorten(text)}' is mostly uppercase.",
                    allowAllCaps
                        ? "Keep the intentional all-caps lockup, but add mixed-case supporting text or smaller captions."
                        : "Use Title Case or sentence case unless the user explicitly asked for all-caps.",
                    info,
                    null));
            }

            float size = textBlock.Size.CurrentValue;
            float spacing = textBlock.Spacing.CurrentValue;
            if (spacing > Math.Max(8, size * 0.18f))
            {
                spacingCount++;
                issues.Add(CreateIssue(
                    "typography",
                    Minor,
                    "Text letter spacing is high enough to read as an outdated motion-graphics trope.",
                    $"Spacing {spacing:F1} on size {size:F1}.",
                    "Reduce tracking, or reserve wide tracking for one short label only.",
                    info,
                    null));
            }
        }

        return new TypographyMetrics(textCount, allCapsCount, spacingCount, 0);
    }

    private static void AnalyzeDesignStructure(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        string? styleProfile,
        bool allowDenseText,
        List<QualityIssue> issues)
    {
        bool highTempoProfile = IsHighTempoProfile(styleProfile);
        SceneObjectInfo[] textObjects = objects.Where(item => item.Object is TextBlock).ToArray();
        SceneObjectInfo[] dominantTextObjects = textObjects
            .Where(item => item.Object is TextBlock textBlock && textBlock.Size.CurrentValue >= 88)
            .ToArray();

        SceneObjectInfo[] concurrentDominantTextObjects = FindMaxConcurrentDominantText(dominantTextObjects);
        if (concurrentDominantTextObjects.Length >= 4)
        {
            issues.Add(new QualityIssue(
                "visualHierarchy",
                Advisory,
                "Too many text elements are styled as dominant focal points.",
                $"{concurrentDominantTextObjects.Length} simultaneously visible text objects use size 88 or larger.",
                "Limit hero-scale type to one primary message and one secondary emphasis; make supporting copy smaller, quieter, or grouped.",
                null,
                concurrentDominantTextObjects.Select(item => item.Element.Id.ToString()).ToArray(),
                concurrentDominantTextObjects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        foreach (SceneObjectInfo info in textObjects)
        {
            var textBlock = (TextBlock)info.Object;
            string text = textBlock.Text.CurrentValue ?? string.Empty;
            int wordCount = CountWords(text);
            int characterCount = text.Count(ch => !char.IsControl(ch) && !char.IsWhiteSpace(ch));
            double durationSeconds = info.Element.Length.TotalSeconds;
            int maxWords = highTempoProfile && durationSeconds is > 0 and <= 1.75 ? 4 : 7;
            int maxCharacters = highTempoProfile && durationSeconds is > 0 and <= 1.75 ? 28 : 43;
            if (durationSeconds is > 0 and < 2.0
                && (wordCount > maxWords || characterCount > maxCharacters))
            {
                bool readIntent = allowDenseText || HasReadingIntent(info);
                issues.Add(CreateIssue(
                    "typographyReadTime",
                    IntentSeverity(readIntent),
                    "A short-lived text element contains more copy than viewers can reliably read.",
                    $"Text '{Shorten(text)}' has {wordCount} words / {characterCount} non-space characters over {durationSeconds:F2}s.",
                    readIntent
                        ? "Intentional dense/long copy is allowed; confirm the copy is legible at playback size, or hold it longer for comfort."
                        : highTempoProfile
                            ? "For 1.5s kinetic beats, keep hero text to 1-3 words and supporting labels to 2-4 words or compact tokens."
                            : "Shorten the copy, split it across beats, or keep it on screen longer with a calmer entrance/exit.",
                    info,
                    info.Element.Start.ToString("c")));
            }
        }

        SceneObjectInfo[] effectHeavyObjects = objects
            .Where(item => !IsBackgroundObject(scene, item))
            .Where(item => item.Object is Drawable drawable
                           && FlattenEffects(drawable.FilterEffect.CurrentValue).Count() >= 3)
            .ToArray();
        if (effectHeavyObjects.Length >= 3)
        {
            issues.Add(new QualityIssue(
                "effectIntent",
                Advisory,
                "Several foreground objects carry dense effect stacks, which can make the look feel arbitrary.",
                $"{effectHeavyObjects.Length} foreground objects have three or more filter effects.",
                "Assign each effect a job such as material texture, hierarchy separation, transition energy, or text legibility; remove decorative repeats.",
                null,
                effectHeavyObjects.Select(item => item.Element.Id.ToString()).ToArray(),
                effectHeavyObjects.Select(item => item.Object.Id.ToString()).ToArray()));
        }
    }

    private static StructureMetrics AnalyzeStructure(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        bool allowMultiObjectElements,
        List<QualityIssue> issues)
    {
        Element[] multiObjectElements = scene.Children
            .Where(element => element.Objects.Count > 1)
            .ToArray();
        Element[] flowMultiObjectElements = multiObjectElements
            .Where(element => element.Objects.Any(obj => obj is IFlowOperator))
            .ToArray();
        Element[] nonFlowMultiObjectElements = multiObjectElements
            .Where(element => element.Objects.All(obj => obj is not IFlowOperator))
            .ToArray();

        Element[] accidentalMultiObjectElements = allowMultiObjectElements
            ? []
            : nonFlowMultiObjectElements.Where(element => !HasCompositeIntent(element)).ToArray();
        Element[] intendedMultiObjectElements = nonFlowMultiObjectElements
            .Except(accidentalMultiObjectElements)
            .ToArray();

        AddElementStructureIssue(accidentalMultiObjectElements, Major, issues);
        AddElementStructureIssue(intendedMultiObjectElements, Advisory, issues);

        SceneObjectInfo[] unclearShapes = objects
            .Where(item => item.Object is Shape)
            .Where(item => !IsBackgroundObject(scene, item))
            .Where(item => !HasShapeIntent(item))
            .Where(item => IsLargeForegroundShape(scene, item) || CountAnimatedProperties(item.Object) > 0)
            .ToArray();
        if (unclearShapes.Length > 0)
        {
            issues.Add(new QualityIssue(
                "shapeIntent",
                Advisory,
                "Foreground shapes need a visible role or purpose before they become large or animated.",
                $"{unclearShapes.Length} large or animated foreground shapes have no recognizable role/purpose naming.",
                "Rename or tag each foreground shape with an explicit role and purpose, such as [role:decorative] beat sweep, [role:text-backing] title plate, or [role:background] surface; delete shapes that do not serve the shot.",
                null,
                unclearShapes.Select(item => item.Element.Id.ToString()).ToArray(),
                unclearShapes.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        SceneObjectInfo[] animatedShapesWithoutMotionIntent = objects
            .Where(item => item.Object is Shape)
            .Where(item => !IsBackgroundObject(scene, item))
            .Where(item => CountAnimatedProperties(item.Object) > 0)
            .Where(item => !HasMotionIntent(item))
            .ToArray();
        if (animatedShapesWithoutMotionIntent.Length > 0)
        {
            issues.Add(new QualityIssue(
                "motionIntent",
                Advisory,
                "Animated foreground shapes need an explicit motion intent.",
                $"{animatedShapesWithoutMotionIntent.Length} animated foreground shapes do not expose motion intent in their Element/Object names.",
                "Name the motion job before export, such as beat slide, scan sweep, pulse reveal, wipe transition, drift texture, or impact burst; remove arbitrary animated shapes.",
                null,
                animatedShapesWithoutMotionIntent.Select(item => item.Element.Id.ToString()).ToArray(),
                animatedShapesWithoutMotionIntent.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        return new StructureMetrics(
            scene.Children.Count,
            multiObjectElements.Length,
            nonFlowMultiObjectElements.Length,
            flowMultiObjectElements.Length,
            unclearShapes.Length,
            animatedShapesWithoutMotionIntent.Length);
    }

    private static void AddElementStructureIssue(
        Element[] elements,
        string severity,
        List<QualityIssue> issues)
    {
        if (elements.Length == 0)
        {
            return;
        }

        issues.Add(new QualityIssue(
            "elementStructure",
            severity,
            "A timeline Element contains multiple EngineObject entries without an IFlowOperator.",
            $"{elements.Length} Elements contain multiple Objects but no DrawableGroup, DrawableDecorator, SoundGroup, Scene3D, or other IFlowOperator.",
            severity == Advisory
                ? "Intentional composite Element is allowed; keep the grouped Objects together only if they truly move as one, otherwise split them or wrap them in an IFlowOperator flow object."
                : "Split ordinary content so each Element owns one EngineObject. Keep multiple Objects in one Element only when the Element contains an IFlowOperator flow object, or tag the Element [role:composite] if the grouping is intentional.",
            null,
            elements.Select(element => element.Id.ToString()).ToArray(),
            elements
                .SelectMany(element => element.Objects)
                .Select(obj => obj.Id.ToString())
                .ToArray()));
    }

    private static TempoMetrics AnalyzeTempo(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        string? styleProfile,
        bool relaxLongHolds,
        List<QualityIssue> issues)
    {
        bool highTempoProfile = IsHighTempoProfile(styleProfile);
        double targetBpm = highTempoProfile ? ResolveTargetBpm(styleProfile) : 0;
        double beatDurationSeconds = targetBpm > 0 ? 60d / targetBpm : 0;
        double durationSeconds = Math.Max(0.001, scene.Duration.TotalSeconds);

        HashSet<long> timelineEvents = [];
        foreach (Element element in scene.Children.Where(element => IsTempoForegroundElement(scene, element)))
        {
            AddEventTime(timelineEvents, element.Start, scene.Duration);
            AddEventTime(timelineEvents, element.Start + element.Length, scene.Duration);
        }

        HashSet<long> keyFrameEvents = [];
        foreach (SceneObjectInfo info in objects.Where(item => IsTempoForegroundObject(scene, item)))
        {
            AddKeyFrameEventTimes(info, scene.Duration, keyFrameEvents);
        }

        double beatRate = targetBpm > 0 ? targetBpm / 60d : 0;
        double requiredTimelineEventsPerSecond = highTempoProfile ? Math.Max(1.0, beatRate / 2d) : 0;
        double requiredTotalEventsPerSecond = highTempoProfile ? Math.Max(1.5, beatRate * 0.85d) : 0;
        double maxForegroundEventGapSeconds = highTempoProfile ? Math.Max(1.6, beatDurationSeconds * 4d) : double.PositiveInfinity;
        Element? finalResolveElement = ResolveFinalForegroundElement(scene, objects);
        (int longForegroundGapCount, double longestForegroundEventGapSeconds) =
            highTempoProfile
                ? AnalyzeForegroundEventGaps(
                    timelineEvents,
                    keyFrameEvents,
                    scene.Duration,
                    maxForegroundEventGapSeconds,
                    finalResolveElement?.Start.TotalSeconds)
                : (0, 0);

        SceneObjectInfo[] slowHoldObjects = highTempoProfile
            ? objects
                .Where(item => IsTempoForegroundObject(scene, item))
                .Where(item => item.Element != finalResolveElement)
                .Where(item => item.Element.Length.TotalSeconds > ResolveMaxHoldSeconds(item, beatDurationSeconds))
                .ToArray()
            : [];
        double longestForegroundHoldSeconds = objects
            .Where(item => IsTempoForegroundObject(scene, item))
            .Select(item => item.Element.Length.TotalSeconds)
            .DefaultIfEmpty(0)
            .Max();

        double timelineEventsPerSecond = timelineEvents.Count / durationSeconds;
        double keyFrameEventsPerSecond = keyFrameEvents.Count / durationSeconds;
        double totalEventsPerSecond = (timelineEvents.Count + keyFrameEvents.Count) / durationSeconds;

        if (highTempoProfile && timelineEventsPerSecond < requiredTimelineEventsPerSecond)
        {
            issues.Add(new QualityIssue(
                "tempoRhythm",
                Advisory,
                "Foreground scene changes are too sparse for the requested BPM.",
                $"Detected {timelineEvents.Count} foreground start/end events over {durationSeconds:F1}s ({timelineEventsPerSecond:F2}/s); required at least {requiredTimelineEventsPerSecond:F2}/s for target {targetBpm:F0} BPM.",
                "Add visible foreground boundaries every 1-2 beats with short typography, strokes, particles, wipes, or other concrete accent Elements. Do not rely on background motion or hidden keyframes to satisfy a fast brief.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                objects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        if (highTempoProfile && totalEventsPerSecond < requiredTotalEventsPerSecond)
        {
            issues.Add(new QualityIssue(
                "tempoRhythm",
                Advisory,
                "The timeline is too sparse for a high-tempo motion-graphics brief.",
                $"Detected {(timelineEvents.Count + keyFrameEvents.Count)} foreground timing/keyframe events over {durationSeconds:F1}s ({totalEventsPerSecond:F2}/s); required at least {requiredTotalEventsPerSecond:F2}/s for target {targetBpm:F0} BPM.",
                "For 120-140 BPM promos, plan beat-grid events around every 1-2 beats: split long shots, add short accent Elements, and add explicit keyframes on transform, opacity, brush, effect, or typography spacing.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                objects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        if (highTempoProfile && longForegroundGapCount > 0)
        {
            issues.Add(new QualityIssue(
                "tempoRhythm",
                Advisory,
                "Foreground event gaps are too long for a high-tempo brief.",
                $"{longForegroundGapCount} foreground event gaps exceed the {maxForegroundEventGapSeconds:F2}s beat-grid target; longest gap {longestForegroundEventGapSeconds:F2}s.",
                "Close long gaps with visible foreground cuts, accent hits, typography swaps, or animated property changes. Background-only drift does not count as fast foreground pacing.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                objects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        if (!relaxLongHolds && slowHoldObjects.Length > 0)
        {
            issues.Add(new QualityIssue(
                "tempoRhythm",
                Advisory,
                "High-tempo foreground beats are held too long.",
                $"{slowHoldObjects.Length} foreground objects exceed the {ResolveNormalMaxHoldSeconds(beatDurationSeconds):F2}s high-tempo hold target; longest hold {longestForegroundHoldSeconds:F2}s.",
                "Keep normal foreground beats near 2-4 beats, reserve longer holds for background texture or a named final resolve, and add interstitial accents when readability requires a longer title hold.",
                null,
                slowHoldObjects.Select(item => item.Element.Id.ToString()).ToArray(),
                slowHoldObjects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        return new TempoMetrics(
            highTempoProfile,
            targetBpm,
            beatDurationSeconds,
            requiredTimelineEventsPerSecond,
            requiredTotalEventsPerSecond,
            timelineEvents.Count,
            timelineEventsPerSecond,
            keyFrameEvents.Count,
            keyFrameEventsPerSecond,
            longForegroundGapCount,
            longestForegroundEventGapSeconds,
            slowHoldObjects.Length,
            longestForegroundHoldSeconds);
    }

    private static ShapeDiversityMetrics AnalyzeShapeDiversity(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        bool allowRectDominance,
        bool relaxDecorativeShapes,
        List<QualityIssue> issues)
    {
        SceneObjectInfo[] rects = objects.Where(item => item.Object is RectShape).ToArray();
        int backgroundRects = rects.Count(item => IsBackgroundRect(scene, item));
        int nonBackgroundRects = rects.Length - backgroundRects;
        int roundedRects = objects.Count(item => item.Object is RoundedRectShape);
        int ellipses = objects.Count(item => item.Object is EllipseShape);
        int geometricCount = objects.Count(item => item.Object is Shape);
        double dominance = geometricCount == 0 ? 0 : rects.Length / (double)geometricCount;
        SceneObjectInfo[] ambiguousDecorativeShapes = objects
            .Where(item => item.Object is Shape)
            .Where(item => !IsBackgroundObject(scene, item))
            .Where(item => IsLargeForegroundShape(scene, item) || CountAnimatedProperties(item.Object) > 0)
            .Where(IsAmbiguousDecorativeShape)
            .ToArray();

        if (!allowRectDominance
            && nonBackgroundRects >= 3
            && dominance >= 0.6)
        {
            issues.Add(new QualityIssue(
                "shapeDiversity",
                Advisory,
                "The scene relies on too many plain RectShape objects outside background use.",
                $"{nonBackgroundRects} non-background RectShape objects; rect dominance {dominance:P0}.",
                "Keep full-frame plates as RectShape, but switch foreground panels to RoundedRectShape, EllipseShape, GeometryShape, media, strokes, or procedural texture.",
                null,
                rects.Select(item => item.Element.Id.ToString()).ToArray(),
                rects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        if (!relaxDecorativeShapes && ambiguousDecorativeShapes.Length > 0)
        {
            issues.Add(new QualityIssue(
                "decorativeShapeClarity",
                Advisory,
                "Abstract decorative light shapes are too ambiguous to read as intentional content.",
                $"{ambiguousDecorativeShapes.Length} large or animated foreground shapes use abstract light/material naming such as glint, glow, bloom, aperture, lens, or glass.",
                "Replace these with concrete visual systems the viewer can parse, such as strokes, particles, letter fragments, UI/editor marks, masks, media, or procedural texture; move purely atmospheric light to [role:background] with soft falloff.",
                null,
                ambiguousDecorativeShapes.Select(item => item.Element.Id.ToString()).ToArray(),
                ambiguousDecorativeShapes.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        return new ShapeDiversityMetrics(
            rects.Length,
            backgroundRects,
            nonBackgroundRects,
            roundedRects,
            ellipses,
            dominance,
            ambiguousDecorativeShapes.Length);
    }

    private static int AnalyzeTextBackgroundFit(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        List<QualityIssue> issues)
    {
        int mismatchCount = 0;
        SceneObjectInfo[] plates = objects
            .Where(item => item.Object is RectShape or RoundedRectShape)
            .Where(item => !IsBackgroundObject(scene, item))
            .Where(IsTextBackingPlate)
            .ToArray();

        foreach (SceneObjectInfo textInfo in objects.Where(item => item.Object is TextBlock))
        {
            ObjectBounds textBounds = GetBounds(scene, textInfo);
            var plateCandidates = plates
                .Where(item => item.Element.ZIndex < textInfo.Element.ZIndex)
                .Where(item => Overlaps(textInfo.Element, item.Element))
                .Select(item => (Info: item, Bounds: GetBounds(scene, item)))
                .Where(item => item.Bounds.Width >= textBounds.Width * 0.85
                               && item.Bounds.Height >= textBounds.Height * 0.85)
                .OrderBy(item => Math.Abs(item.Info.Element.ZIndex - textInfo.Element.ZIndex))
                .ThenBy(item => Distance(item.Bounds.CenterX, item.Bounds.CenterY, textBounds.CenterX, textBounds.CenterY))
                .ToArray();

            if (plateCandidates.Length == 0)
            {
                continue;
            }

            SceneObjectInfo backingPlate = plateCandidates[0].Info;
            ObjectBounds plateBounds = GetBounds(scene, backingPlate);
            double centerDistance = Distance(plateBounds.CenterX, plateBounds.CenterY, textBounds.CenterX, textBounds.CenterY);
            double requiredPadX = Math.Max(36, textBounds.Width * 0.12);
            double requiredPadY = Math.Max(18, textBounds.Height * 0.18);
            bool centered = centerDistance <= Math.Max(36, Math.Min(plateBounds.Width, plateBounds.Height) * 0.18);
            bool padded = plateBounds.Width >= textBounds.Width + (requiredPadX * 2)
                          && plateBounds.Height >= textBounds.Height + (requiredPadY * 2);
            bool sameTime = Math.Abs((textInfo.Element.Start - backingPlate.Element.Start).TotalSeconds) <= 0.08
                            && Math.Abs((textInfo.Element.Length - backingPlate.Element.Length).TotalSeconds) <= 0.08;
            if (centered && padded && sameTime)
            {
                continue;
            }

            mismatchCount++;
            issues.Add(new QualityIssue(
                "textBackgroundFit",
                Advisory,
                "A background plate behind text is not aligned with the text timing or geometry.",
                $"Center distance {centerDistance:F1}px, plate {plateBounds.Width:F0}x{plateBounds.Height:F0}, text {textBounds.Width:F0}x{textBounds.Height:F0}, matching time range: {sameTime}.",
                "Pair text and backing plate by name or [role:text-backing], matching Start/Length, center transform, and at least 12% horizontal plus 18% vertical padding. Mark decorative rectangles [role:decorative] or use non-rectangular accents so they are not treated as backing plates.",
                textInfo.Element.Start.ToString("c"),
                [textInfo.Element.Id.ToString(), backingPlate.Element.Id.ToString()],
                [textInfo.Object.Id.ToString(), backingPlate.Object.Id.ToString()]));
        }

        return mismatchCount;
    }

    private static PaletteMetrics AnalyzePalette(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        bool allowMonochrome,
        List<QualityIssue> issues)
    {
        Color[] colors = objects
            .SelectMany(item => ExtractColors(item.Object))
            .Where(color => color.A > 12)
            .Distinct()
            .ToArray();

        if (colors.Length == 0)
        {
            (int emptyHardObjects, int emptyHardTransitions) = AnalyzeGradientFalloff(scene, objects, issues);
            return new PaletteMetrics(
                0,
                0,
                0,
                0,
                false,
                false,
                false,
                emptyHardObjects,
                emptyHardTransitions,
                1,
                "monochromatic",
                1,
                1,
                1,
                false);
        }

        Hsv[] hsv = colors.Select(color => color.ToHsv()).ToArray();
        PaletteHarmonyEvaluation harmony = ColorHarmonyEngine.EvaluatePalette(colors.Select(ToHexArgb));
        double averageSaturation = hsv.Average(item => item.S);
        double maxSaturation = hsv.Max(item => item.S);
        double minLuma = colors.Min(RelativeLuma);
        double maxLuma = colors.Max(RelativeLuma);
        double lumaRange = maxLuma - minLuma;
        bool hasDarkTeal = colors.Any(color => RelativeLuma(color) < 0.16 && HueIn(color, 160, 230));
        bool hasCyan = colors.Any(color => color.ToHsv() is { S: >= 55, V: >= 55 } && HueIn(color, 175, 210));
        bool hasMagenta = colors.Any(color => color.ToHsv() is { S: >= 55, V: >= 50 } && HueIn(color, 285, 335));
        bool darkTealCyanMagenta = hasDarkTeal && hasCyan && hasMagenta;
        bool oversaturated = colors.Length >= 3 && averageSaturation >= 68 && maxSaturation >= 88;
        bool lowContrast = colors.Length >= 2 && lumaRange < 0.18;

        if (darkTealCyanMagenta)
        {
            issues.Add(new QualityIssue(
                "paletteHarmony",
                Advisory,
                "The palette repeats the dark teal plus cyan/magenta look that the guidance marks as overused.",
                "Detected a dark teal base with saturated cyan and magenta accents.",
                "Use a neutral base, one restrained accent, and a separate text color with clear luma contrast.",
                null,
                [],
                []));
        }
        else if (oversaturated)
        {
            issues.Add(new QualityIssue(
                "paletteHarmony",
                Advisory,
                "The palette has too many saturated colors competing for attention.",
                $"Average saturation {averageSaturation:F1}, max saturation {maxSaturation:F1}.",
                "Reduce to one saturated accent and use muted support colors for background and plates.",
                null,
                [],
                []));
        }

        bool monochromeIntent = allowMonochrome || AnyMonochromeIntent(objects);
        if (lowContrast && !monochromeIntent)
        {
            issues.Add(new QualityIssue(
                "paletteHarmony",
                Advisory,
                "The sampled object colors have low luma separation, which risks unreadable text and muddy layers.",
                $"Luma range {lumaRange:F2}.",
                "Separate background, text, and accent roles with stronger luma contrast.",
                null,
                [],
                []));
        }

        bool lowHarmony = !harmony.IsHarmonious && !monochromeIntent;
        if (lowHarmony)
        {
            issues.Add(new QualityIssue(
                "paletteHarmony",
                Advisory,
                "The palette does not land cleanly on a recognized hue-wheel relationship.",
                $"Harmony score {harmony.Score:F2} ({harmony.BestScheme}); hue {harmony.HueRelationshipScore:F2}, saturation {harmony.SaturationBalanceScore:F2}, luma {harmony.LumaBalanceScore:F2}.",
                "Re-derive the palette from a clear base hue using analogous, complementary, split-complementary, triadic, tetradic, or monochromatic roles; keep one saturated accent and preserve text/background luma separation.",
                null,
                [],
                []));
        }

        (int hardGradientObjectCount, int hardGradientTransitionCount) = AnalyzeGradientFalloff(scene, objects, issues);

        return new PaletteMetrics(
            colors.Length,
            averageSaturation,
            maxSaturation,
            lumaRange,
            darkTealCyanMagenta,
            oversaturated,
            lowContrast,
            hardGradientObjectCount,
            hardGradientTransitionCount,
            harmony.Score,
            harmony.BestScheme,
            harmony.HueRelationshipScore,
            harmony.SaturationBalanceScore,
            harmony.LumaBalanceScore,
            lowHarmony);
    }

    private static BackgroundRichnessMetrics AnalyzeBackgroundRichness(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        List<QualityIssue> issues)
    {
        SceneObjectInfo[] fullFrameBackgrounds = objects
            .Where(item => IsFullFrameBackground(scene, item))
            .ToArray();
        if (fullFrameBackgrounds.Length == 0)
        {
            return new BackgroundRichnessMetrics(0, 0, 0);
        }

        SceneObjectInfo[] flatSingleLayerBackgrounds = fullFrameBackgrounds
            .GroupBy(item => item.Element)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(item => CountGradientStops(item.Object) <= 2)
            .Where(item => CountAnimatedProperties(item.Object) == 0)
            .Where(item => !HasProceduralBackgroundTexture(item.Object))
            .ToArray();
        if (fullFrameBackgrounds.Length == 1 && flatSingleLayerBackgrounds.Length == 1)
        {
            SceneObjectInfo info = flatSingleLayerBackgrounds[0];
            int stops = CountGradientStops(info.Object);
            issues.Add(new QualityIssue(
                "backgroundRichness",
                Advisory,
                "The full-frame background is a flat single layer.",
                $"One full-frame background layer uses {stops} gradient stops and has no animated background property or procedural texture.",
                "Add at least one derived depth layer, use a 3+ stop gradient or shader texture, and animate subtle drift/parallax unless the brief explicitly calls for a still minimal field.",
                null,
                [info.Element.Id.ToString()],
                [info.Object.Id.ToString()]));
        }

        int richCount = fullFrameBackgrounds.Length - flatSingleLayerBackgrounds.Length;
        return new BackgroundRichnessMetrics(
            fullFrameBackgrounds.Length,
            flatSingleLayerBackgrounds.Length,
            richCount);
    }

    private static LayerDensityMetrics AnalyzeLayerDensity(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        string? styleProfile,
        bool allowMinimalDensity,
        double plannedForegroundElementsPerShot,
        List<QualityIssue> issues)
    {
        bool motionGraphicsIntent = HasMotionGraphicsIntent(scene, objects, styleProfile);
        double durationSeconds = Math.Max(0.001, scene.Duration.TotalSeconds);
        int bandCount = Math.Clamp((int)Math.Ceiling(durationSeconds / 1.5), 1, 12);
        double bandSeconds = durationSeconds / bandCount;
        var bands = new List<LayerDensityBandMetrics>(bandCount);
        for (int index = 0; index < bandCount; index++)
        {
            double startSeconds = index * bandSeconds;
            double endSeconds = index == bandCount - 1 ? durationSeconds : startSeconds + bandSeconds;
            TimeSpan start = TimeSpan.FromSeconds(startSeconds);
            TimeSpan end = TimeSpan.FromSeconds(endSeconds);
            SceneObjectInfo[] active = objects
                .Where(item => IsVisibleDuring(item, start, end))
                .Where(item => IsLayerDensityObject(item.Object))
                .ToArray();
            int visibleLayerCount = active.Length;
            int foregroundLayerCount = active.Count(item => ClassifyDepthBand(scene, item) == DepthBand.Foreground);
            bool hasBackground = active.Any(item => ClassifyDepthBand(scene, item) == DepthBand.Background);
            bool hasMidground = active.Any(item => ClassifyDepthBand(scene, item) == DepthBand.Midground);
            bool hasForeground = foregroundLayerCount > 0;
            bands.Add(new LayerDensityBandMetrics(
                Math.Round(startSeconds, 3, MidpointRounding.AwayFromZero),
                Math.Round(endSeconds, 3, MidpointRounding.AwayFromZero),
                visibleLayerCount,
                foregroundLayerCount,
                hasBackground,
                hasMidground,
                hasForeground));
        }

        double averageVisibleLayers = bands.Average(band => band.VisibleLayerCount);
        int minimumVisibleLayers = bands.Min(band => band.VisibleLayerCount);
        int maximumVisibleLayers = bands.Max(band => band.VisibleLayerCount);
        double averageForegroundLayers = bands.Average(band => band.ForegroundLayerCount);
        int minimumForegroundLayers = bands.Min(band => band.ForegroundLayerCount);
        int maximumForegroundLayers = bands.Max(band => band.ForegroundLayerCount);
        int bandsWithBackground = bands.Count(band => band.HasBackground);
        int bandsWithMidground = bands.Count(band => band.HasMidground);
        int bandsWithForeground = bands.Count(band => band.HasForeground);
        int bandsWithAllDepthBands = bands.Count(band => band.HasBackground && band.HasMidground && band.HasForeground);
        double plannedForeground = Math.Max(0, plannedForegroundElementsPerShot);
        double halfFloor = plannedForeground > 0 ? plannedForeground / 2d : 0;
        int bandsBelowHalfPlan = halfFloor > 0
            ? bands.Count(band => band.ForegroundLayerCount + 0.0001 < halfFloor)
            : 0;
        bool densityPlanViolation = motionGraphicsIntent && bandsBelowHalfPlan > 0;
        bool minimalIntent = allowMinimalDensity || IsMinimalProfile(styleProfile) || AnyMinimalDensityIntent(objects);

        if (motionGraphicsIntent
            && (averageVisibleLayers < 3
                || bandsWithAllDepthBands == 0
                || bandsWithForeground < bandCount))
        {
            issues.Add(new QualityIssue(
                "layerDensity",
                Advisory,
                "The motion-graphics scene has thin layer density or incomplete depth coverage.",
                $"Average visible layers {averageVisibleLayers:F1}; all three depth bands present in {bandsWithAllDepthBands}/{bandCount} time bands; foreground present in {bandsWithForeground}/{bandCount}.",
                minimalIntent
                    ? "Intentional minimal density is allowed; verify the negative space is part of the brief and not an omitted layer stack."
                    : "Add visible background, midground, and foreground layers per shot, such as surface texture, depth accents, typography, particles, strokes, or concrete UI/editor marks.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                objects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        if (densityPlanViolation)
        {
            issues.Add(new QualityIssue(
                "layerDensity",
                IntentSeverity(minimalIntent),
                "Authored foreground density falls below half of the quantitative plan.",
                $"{bandsBelowHalfPlan}/{bandCount} time bands have fewer than {halfFloor:F1} foreground layers; planned foreground elements per shot {plannedForeground:F1}, minimum authored foreground layers {minimumForegroundLayers}.",
                minimalIntent
                    ? "Intentional sparse/minimal density is allowed; record why the authored result intentionally departs from the quantitativePlanSheet before export."
                    : "Add the missing foreground layers or revise the quantitativePlanSheet. Do not export a motion-graphics scene whose authored density shrank below half of the plan.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                objects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        return new LayerDensityMetrics(
            motionGraphicsIntent,
            bandCount,
            Math.Round(bandSeconds, 3, MidpointRounding.AwayFromZero),
            Math.Round(averageVisibleLayers, 3, MidpointRounding.AwayFromZero),
            minimumVisibleLayers,
            maximumVisibleLayers,
            Math.Round(averageForegroundLayers, 3, MidpointRounding.AwayFromZero),
            minimumForegroundLayers,
            maximumForegroundLayers,
            bandsWithBackground,
            bandsWithMidground,
            bandsWithForeground,
            bandsWithAllDepthBands,
            bandsBelowHalfPlan,
            Math.Round(plannedForeground, 3, MidpointRounding.AwayFromZero),
            Math.Round(halfFloor, 3, MidpointRounding.AwayFromZero),
            densityPlanViolation,
            bands);
    }

    private static void AnalyzeMaterialUiLook(
        IReadOnlyList<SceneObjectInfo> objects,
        bool relaxAesthetics,
        List<QualityIssue> issues)
    {
        if (relaxAesthetics)
        {
            return;
        }

        SceneObjectInfo[] cardLikeObjects = objects
            .Where(item => item.Object is RectShape or RoundedRectShape)
            .Where(item => HasHeavyCardEffects(item.Object))
            .ToArray();

        if (cardLikeObjects.Length < 2)
        {
            return;
        }

        issues.Add(new QualityIssue(
            "materialUiLook",
            Advisory,
            "Multiple rounded or rectangular cards use heavy shadow/blur styling, which reads like outdated Material UI.",
            $"{cardLikeObjects.Length} card-like shapes have heavy DropShadow or Blur effects.",
            "Use flatter editorial plates, texture, line work, image masks, or subtle single shadows instead of repeated card surfaces.",
            null,
            cardLikeObjects.Select(item => item.Element.Id.ToString()).ToArray(),
            cardLikeObjects.Select(item => item.Object.Id.ToString()).ToArray()));
    }

    private async ValueTask<MotionContinuityMetrics> AnalyzeMotionAsync(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        IReadOnlyList<TimeSpan>? timeSeconds,
        int sampleCount,
        float renderScale,
        bool allowHardCuts,
        bool relaxShortSegments,
        bool allowStillness,
        bool evaluateMotion,
        List<QualityIssue> issues,
        CancellationToken cancellationToken)
    {
        int animatedPropertyCount = objects.Sum(item => CountAnimatedProperties(item.Object));
        int shortSegmentCount = scene.Children.Count(element =>
            element.Length > TimeSpan.Zero && element.Length < TimeSpan.FromSeconds(0.45));
        int hardCutCount = CountHardCutLikeBoundaries(scene);
        if (!allowHardCuts && hardCutCount >= 2)
        {
            issues.Add(new QualityIssue(
                "cutRhythm",
                Advisory,
                "The timeline has repeated hard-cut-like element boundaries without enough bridging animation.",
                $"{hardCutCount} interior boundaries start or end visible elements with no detected opacity/transform animation.",
                "Bridge cuts with short overlap, opacity animation, transform continuation, or an intentional rhythm note in the brief.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                []));
        }

        if (!relaxShortSegments && shortSegmentCount >= 3)
        {
            issues.Add(new QualityIssue(
                "cutRhythm",
                Advisory,
                "Several timeline segments are very short, which can produce a chopped-up edit.",
                $"{shortSegmentCount} elements are shorter than 0.45 seconds.",
                "Group micro-beats into a clearer phrase, or add visible transition continuity between them.",
                null,
                scene.Children
                    .Where(element => element.Length > TimeSpan.Zero && element.Length < TimeSpan.FromSeconds(0.45))
                    .Select(element => element.Id.ToString())
                    .ToArray(),
                []));
        }

        if (!evaluateMotion)
        {
            return new MotionContinuityMetrics(false, null, 0, 0, hardCutCount, shortSegmentCount, animatedPropertyCount);
        }

        IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
        MotionVariationResponse motion = await motionVariationAnalyzer.AnalyzeAsync(
            scene,
            sampleTimes,
            renderScale,
            0.02,
            48,
            0.35,
            0.90,
            24,
            cancellationToken).ConfigureAwait(false);

        if (!motion.PassesMinimumMotion)
        {
            bool stillnessIntent = allowStillness || AnyStillnessIntent(objects);
            issues.Add(new QualityIssue(
                "motionContinuity",
                IntentSeverity(stillnessIntent),
                motion.Verdict == "low-motion-variation"
                    ? "Rendered samples have too little temporal change."
                    : "Rendered samples keep visible content too sparse or confined.",
                $"Motion verdict {motion.Verdict}; minimum changed-pixel ratio {motion.MinimumChangedPixelRatio:P2}.",
                stillnessIntent
                    ? "Intentional stillness/held frame is allowed; confirm the held composition reads as deliberate (negative space, single focal point) rather than a stalled render."
                    : "Revise with connected phase changes across transform, opacity, brush/effect parameters, and foreground/background motion before export.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                []));
        }
        else if (animatedPropertyCount == 0 && scene.Duration >= TimeSpan.FromSeconds(2))
        {
            issues.Add(new QualityIssue(
                "motionContinuity",
                Minor,
                "Rendered samples changed, but no explicit animated properties were found.",
                "The edit may rely only on timeline cuts instead of continuous motion.",
                "Add at least one intentional animated property family, such as transform, opacity, brush, effect, or text spacing.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                []));
        }

        return new MotionContinuityMetrics(
            true,
            motion.Verdict,
            motion.MinimumChangedPixelRatio,
            motion.AverageChangedPixelRatio,
            hardCutCount,
            shortSegmentCount,
            animatedPropertyCount);
    }

    private static IReadOnlyList<TimeSpan> ResolveSampleTimes(Scene scene, IReadOnlyList<TimeSpan>? timeSeconds, int sampleCount)
    {
        if (timeSeconds is { Count: > 0 })
        {
            TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
            TimeSpan[] explicitTimes = timeSeconds
                .Select(time => time < TimeSpan.Zero ? TimeSpan.Zero : time)
                .Select(time => time > duration ? duration : time)
                .Distinct()
                .OrderBy(time => time)
                .ToArray();
            if (explicitTimes.Length >= 2)
            {
                return explicitTimes;
            }
        }

        int count = Math.Clamp(sampleCount, 2, 8);
        double durationSeconds = scene.Duration > TimeSpan.Zero ? scene.Duration.TotalSeconds : 1;
        return Enumerable
            .Range(0, count)
            .Select(index => TimeSpan.FromSeconds(durationSeconds * (index + 0.5) / count))
            .ToArray();
    }

    private static IEnumerable<SceneObjectInfo> EnumerateObjects(Scene scene)
    {
        foreach (Element element in scene.Children)
        {
            foreach (EngineObject obj in element.Objects)
            {
                yield return new SceneObjectInfo(element, obj);
            }
        }
    }

    private static bool LooksLikeLongAllCaps(string text)
    {
        int letters = text.Count(char.IsLetter);
        if (letters < 7)
        {
            return false;
        }

        int uppercase = text.Count(char.IsUpper);
        int lowercase = text.Count(char.IsLower);
        return lowercase == 0 && uppercase / (double)letters >= 0.85;
    }

    private static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }

    private static bool IsBackgroundRect(Scene scene, SceneObjectInfo info)
    {
        if (info.Object is not RectShape rect)
        {
            return false;
        }

        ObjectBounds bounds = GetBounds(scene, info);
        double sceneArea = Math.Max(1, scene.FrameSize.Width * scene.FrameSize.Height);
        double rectArea = bounds.Width * bounds.Height;
        if (HasRole(info, "background", "surface", "backdrop", "field"))
        {
            return true;
        }

        return rectArea >= sceneArea * 0.72
               || info.Element.ZIndex <= 1
               || ContainsAny(info.Element.Name, "background", "field", "backdrop", "surface")
               || ContainsAny(rect.Name, "background", "field", "backdrop", "surface");
    }

    private static bool IsBackgroundObject(Scene scene, SceneObjectInfo info)
    {
        if (IsBackgroundRect(scene, info))
        {
            return true;
        }

        ObjectBounds bounds = GetBounds(scene, info);
        double sceneArea = Math.Max(1, scene.FrameSize.Width * scene.FrameSize.Height);
        double objectArea = bounds.Width * bounds.Height;
        if (HasRole(info, "background", "surface", "backdrop", "field"))
        {
            return true;
        }

        return objectArea >= sceneArea * 0.72
               || info.Element.ZIndex <= 1
               || ContainsAny(info.Element.Name, "background", "field", "backdrop", "surface")
               || ContainsAny(info.Object.Name, "background", "field", "backdrop", "surface");
    }

    private static bool IsFullFrameBackground(Scene scene, SceneObjectInfo info)
    {
        if (!IsBackgroundObject(scene, info))
        {
            return false;
        }

        ObjectBounds bounds = GetBounds(scene, info);
        double sceneArea = Math.Max(1, scene.FrameSize.Width * scene.FrameSize.Height);
        double objectArea = bounds.Width * bounds.Height;
        return objectArea >= sceneArea * 0.85
               || (bounds.Width >= scene.FrameSize.Width * 0.90
                   && bounds.Height >= scene.FrameSize.Height * 0.90);
    }

    private static int CountGradientStops(EngineObject obj)
    {
        return obj switch
        {
            Shape shape when shape.Fill.CurrentValue is GradientBrush gradient => gradient.GradientStops.Count,
            TextBlock text when text.Fill.CurrentValue is GradientBrush gradient => gradient.GradientStops.Count,
            _ => 0
        };
    }

    private static bool HasProceduralBackgroundTexture(EngineObject obj)
    {
        return obj is Drawable drawable
               && FlattenEffects(drawable.FilterEffect.CurrentValue).Any(effect => effect is SKSLScriptEffect);
    }

    private static bool HasMotionGraphicsIntent(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        string? styleProfile)
    {
        if (ContainsAny(
                styleProfile,
                "motion",
                "motion-graphics",
                "motion graphics",
                "kinetic",
                "promo",
                "title",
                "logo reveal",
                "infographic",
                "opener"))
        {
            return true;
        }

        if (ContainsAny(scene.Name, "motion", "kinetic", "promo", "title", "logo reveal", "infographic"))
        {
            return true;
        }

        return objects.Any(item => ContainsAny(
            $"{item.Element.Name} {item.Object.Name}",
            "motion",
            "kinetic",
            "promo",
            "title sequence",
            "logo reveal",
            "infographic"));
    }

    private static bool IsMinimalProfile(string? styleProfile)
    {
        return ContainsAny(
            styleProfile,
            "minimal",
            "minimalism",
            "sparse",
            "negative-space",
            "negative space",
            "poster",
            "quiet");
    }

    private static bool AnyMinimalDensityIntent(IReadOnlyList<SceneObjectInfo> objects)
        => objects.Any(HasMinimalDensityIntent);

    private static bool HasMinimalDensityIntent(SceneObjectInfo info)
    {
        return HasRole(info, "minimal", "minimalism", "sparse", "negative-space", "poster", "quiet")
               || ContainsAny(info.Element.Name, "minimal", "minimalism", "sparse", "negative space", "negative-space", "poster field", "quiet field")
               || ContainsAny(info.Object.Name, "minimal", "minimalism", "sparse", "negative space", "negative-space", "poster field", "quiet field");
    }

    private static bool IsVisibleDuring(SceneObjectInfo info, TimeSpan start, TimeSpan end)
    {
        if (info.Element.Length <= TimeSpan.Zero)
        {
            return false;
        }

        if (info.Element.Start >= end || info.Element.Start + info.Element.Length <= start)
        {
            return false;
        }

        return info.Object is not Drawable drawable
               || drawable.Opacity.CurrentValue > 0.01;
    }

    private static bool IsLayerDensityObject(EngineObject obj)
    {
        return obj is not PortalObject
               && obj is Drawable;
    }

    private static DepthBand ClassifyDepthBand(Scene scene, SceneObjectInfo info)
    {
        if (IsBackgroundObject(scene, info))
        {
            return DepthBand.Background;
        }

        string name = $"{info.Element.Name} {info.Object.Name}";
        if (HasRole(info, "foreground", "hero", "focal", "primary", "text", "accent")
            || info.Object is TextBlock
            || info.Element.ZIndex >= 8
            || ContainsAny(name, "foreground", "hero", "focal", "primary", "title", "logo", "caption", "label", "accent hit"))
        {
            return DepthBand.Foreground;
        }

        return DepthBand.Midground;
    }

    private static bool HasShapeIntent(SceneObjectInfo info)
    {
        return HasRole(
                   info,
                   "background",
                   "surface",
                   "backdrop",
                   "field",
                   "text-backing",
                   "decorative",
                   "accent",
                   "texture",
                   "transition",
                   "rhythm")
               || ContainsIntentToken(info.Element.Name)
               || ContainsIntentToken(info.Object.Name);
    }

    private static bool HasMotionIntent(SceneObjectInfo info)
    {
        return HasRole(info, "motion", "decorative", "transition", "rhythm", "accent", "texture")
               || ContainsMotionToken(info.Element.Name)
               || ContainsMotionToken(info.Object.Name);
    }

    private static bool AnyStillnessIntent(IReadOnlyList<SceneObjectInfo> objects)
        => objects.Any(HasStillnessIntent);

    private static bool HasStillnessIntent(SceneObjectInfo info)
    {
        return HasRole(info, "still", "stillness", "hold", "static", "freeze", "negative-space", "poster", "minimal")
               || ContainsAny(info.Element.Name, "stillness", "hold frame", "held frame", "freeze frame", "negative space", "static hold", "poster frame", "held still")
               || ContainsAny(info.Object.Name, "stillness", "hold frame", "held frame", "freeze frame", "negative space", "static hold", "poster frame", "held still");
    }

    private static bool HasReadingIntent(SceneObjectInfo info)
    {
        return HasRole(info, "reading", "long-read", "dense-copy", "body-copy", "manifesto", "credits", "legal", "paragraph")
               || ContainsAny(info.Element.Name, "manifesto", "credits", "legal", "long read", "long-read", "dense copy", "reading block", "paragraph")
               || ContainsAny(info.Object.Name, "manifesto", "credits", "legal", "long read", "long-read", "dense copy", "reading block", "paragraph");
    }

    private static bool HasCompositeIntent(Element element)
    {
        return HasRole(element.Name, "composite")
               || HasRole(element.Name, "layered")
               || HasRole(element.Name, "grouped")
               || HasRole(element.Name, "flow")
               || ContainsAny(element.Name, "composite element", "layered stack", "grouped layers");
    }

    private static bool AnyMonochromeIntent(IReadOnlyList<SceneObjectInfo> objects)
        => objects.Any(HasMonochromeIntent);

    private static bool HasMonochromeIntent(SceneObjectInfo info)
    {
        return HasRole(info, "monochrome", "monochromatic", "low-contrast", "grayscale", "greyscale", "tonal", "duotone")
               || ContainsAny(info.Element.Name, "monochrome", "monochromatic", "low contrast", "low-contrast", "grayscale", "greyscale", "tonal", "duotone")
               || ContainsAny(info.Object.Name, "monochrome", "monochromatic", "low contrast", "low-contrast", "grayscale", "greyscale", "tonal", "duotone");
    }

    private static bool IsAmbiguousDecorativeShape(SceneObjectInfo info)
    {
        string name = $"{info.Element.Name} {info.Object.Name}";
        bool decorative = HasRole(info, "decorative", "accent")
                          || ContainsAny(name, "glint", "glow", "bloom", "aperture", "lens", "flare", "halo", "shimmer", "glass", "reflection", "refraction", "gold");
        if (!decorative)
        {
            return false;
        }

        bool abstractLight = ContainsAny(name, "glint", "glow", "bloom", "aperture", "lens", "flare", "halo", "shimmer", "glass", "reflection", "refraction", "gold");
        bool concreteVisualSystem = ContainsAny(
            name,
            "stroke",
            "line",
            "particle",
            "node",
            "grid",
            "letter",
            "type",
            "glyph",
            "cursor",
            "underline",
            "divider",
            "mask",
            "matte",
            "wipe",
            "editor",
            "timeline",
            "keyframe",
            "handle",
            "logo");

        return abstractLight
               && (info.Object is EllipseShape || !concreteVisualSystem);
    }

    private static bool ContainsIntentToken(string? value)
    {
        return ContainsAny(
            value,
            "background",
            "backdrop",
            "surface",
            "field",
            "plate",
            "backing",
            "accent",
            "rhythm",
            "beat",
            "mark",
            "tick",
            "stroke",
            "line",
            "slash",
            "glint",
            "light",
            "scan",
            "texture",
            "grain",
            "noise",
            "particle",
            "node",
            "grid",
            "frame",
            "border",
            "mask",
            "matte",
            "glass",
            "reflection",
            "refract",
            "shadow",
            "wipe",
            "transition",
            "burst",
            "trail",
            "flow",
            "guide",
            "crop",
            "focus",
            "divider",
            "separator",
            "underline",
            "cursor",
            "highlight");
    }

    private static bool ContainsMotionToken(string? value)
    {
        return ContainsAny(
            value,
            "motion",
            "animate",
            "animation",
            "beat",
            "rhythm",
            "slide",
            "drift",
            "sweep",
            "scan",
            "pulse",
            "reveal",
            "wipe",
            "transition",
            "burst",
            "impact",
            "snap",
            "lock",
            "flicker",
            "strobe",
            "tick",
            "shutter",
            "parallax",
            "flow",
            "trail",
            "glitch",
            "type-on",
            "resolve");
    }

    private static bool IsLargeForegroundShape(Scene scene, SceneObjectInfo info)
    {
        ObjectBounds bounds = GetBounds(scene, info);
        double sceneArea = Math.Max(1, scene.FrameSize.Width * scene.FrameSize.Height);
        return (bounds.Width * bounds.Height) >= sceneArea * 0.045;
    }

    private static bool IsTempoForegroundElement(Scene scene, Element element)
    {
        return element.Objects.Any(obj => IsTempoForegroundObject(scene, new SceneObjectInfo(element, obj)));
    }

    private static Element? ResolveFinalForegroundElement(Scene scene, IReadOnlyList<SceneObjectInfo> objects)
    {
        SceneObjectInfo[] foreground = objects
            .Where(item => IsTempoForegroundObject(scene, item))
            .ToArray();
        if (foreground.Length == 0)
        {
            return null;
        }

        SceneObjectInfo? tagged = foreground
            .Where(IsNamedFinalResolve)
            .Select(item => (SceneObjectInfo?)item)
            .FirstOrDefault();
        if (tagged is { } taggedInfo)
        {
            return taggedInfo.Element;
        }

        SceneObjectInfo last = foreground
            .OrderByDescending(item => item.Element.Start)
            .First();
        return last.Element.Start > TimeSpan.Zero ? last.Element : null;
    }

    private static bool IsNamedFinalResolve(SceneObjectInfo info)
        => HasRole(info, "resolve", "final")
           || ContainsAny(info.Element.Name, "final", "resolve", "outro", "ending", "logo lock")
           || ContainsAny(info.Object.Name, "final", "resolve", "outro", "ending", "logo lock");

    private static bool IsTempoForegroundObject(Scene scene, SceneObjectInfo info)
    {
        return !IsBackgroundObject(scene, info)
               && info.Object is not PortalObject
               && !IsAmbientSupport(info);
    }

    private static bool IsAmbientSupport(SceneObjectInfo info)
    {
        return HasRole(info, "background", "surface", "field", "texture")
               || ContainsAny(info.Element.Name, "background", "surface", "field", "texture", "grain", "noise")
               || ContainsAny(info.Object.Name, "background", "surface", "field", "texture", "grain", "noise");
    }

    private static double ResolveMaxHoldSeconds(SceneObjectInfo info, double beatDurationSeconds)
    {
        double normal = ResolveNormalMaxHoldSeconds(beatDurationSeconds);
        if (ContainsAny(info.Element.Name, "final", "resolve", "outro", "ending", "logo lock")
            || ContainsAny(info.Object.Name, "final", "resolve", "outro", "ending", "logo lock"))
        {
            return Math.Max(normal, beatDurationSeconds * 8);
        }

        return normal;
    }

    private static double ResolveNormalMaxHoldSeconds(double beatDurationSeconds)
    {
        return Math.Max(1.6, beatDurationSeconds * 4d);
    }

    private static double ResolveTargetBpm(string? styleProfile)
    {
        double? explicitBpm = ExtractBpm(styleProfile);
        if (explicitBpm is { } bpm)
        {
            return bpm;
        }

        return 130;
    }

    private static double? ExtractBpm(string? styleProfile)
    {
        if (string.IsNullOrWhiteSpace(styleProfile))
        {
            return null;
        }

        double? firstBpm = null;
        int index = 0;
        while (index < styleProfile.Length)
        {
            if (!char.IsDigit(styleProfile[index]))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < styleProfile.Length && char.IsDigit(styleProfile[index]))
            {
                index++;
            }

            if (double.TryParse(styleProfile[start..index], out double value)
                && value is >= 80 and <= 220)
            {
                firstBpm ??= value;
                if (value is >= 120 and <= 140)
                {
                    return value;
                }
            }
        }

        return firstBpm;
    }

    private static void AddEventTime(HashSet<long> events, TimeSpan time, TimeSpan duration)
    {
        if (time < TimeSpan.Zero || time > duration)
        {
            return;
        }

        long quantizedMilliseconds = (long)Math.Round(time.TotalMilliseconds / 40d) * 40;
        events.Add(quantizedMilliseconds);
    }

    private static void AddKeyFrameEventTimes(SceneObjectInfo info, TimeSpan sceneDuration, HashSet<long> events)
    {
        AddKeyFrameEventTimes(info.Object, info.Element.Start, sceneDuration, events);
    }

    private static void AddKeyFrameEventTimes(
        EngineObject obj,
        TimeSpan elementStart,
        TimeSpan sceneDuration,
        HashSet<long> events)
    {
        foreach (IProperty property in obj.Properties)
        {
            if (property.Animation is IKeyFrameAnimation animation)
            {
                foreach (IKeyFrame keyFrame in animation.KeyFrames)
                {
                    TimeSpan time = animation.UseGlobalClock
                        ? keyFrame.KeyTime
                        : elementStart + keyFrame.KeyTime;
                    AddEventTime(events, time, sceneDuration);
                }
            }

            switch (property.CurrentValue)
            {
                case EngineObject nested:
                    AddKeyFrameEventTimes(nested, elementStart, sceneDuration, events);
                    break;
                case ICoreList list:
                    foreach (object? item in list)
                    {
                        if (item is EngineObject nestedItem)
                        {
                            AddKeyFrameEventTimes(nestedItem, elementStart, sceneDuration, events);
                        }
                    }

                    break;
            }
        }
    }

    private static (int LongGapCount, double LongestGapSeconds) AnalyzeForegroundEventGaps(
        HashSet<long> timelineEvents,
        HashSet<long> keyFrameEvents,
        TimeSpan sceneDuration,
        double maxGapSeconds,
        double? finalResolveStartSeconds = null)
    {
        double durationSeconds = Math.Max(0.001, sceneDuration.TotalSeconds);
        double[] eventSeconds = timelineEvents
            .Concat(keyFrameEvents)
            .Select(milliseconds => milliseconds / 1000d)
            .Append(0)
            .Append(durationSeconds)
            .Where(seconds => seconds >= 0 && seconds <= durationSeconds)
            .Distinct()
            .Order()
            .ToArray();

        int longGapCount = 0;
        double longestGapSeconds = 0;
        for (int i = 1; i < eventSeconds.Length; i++)
        {
            double gap = eventSeconds[i] - eventSeconds[i - 1];
            if (finalResolveStartSeconds is { } resolveStart
                && eventSeconds[i - 1] >= resolveStart)
            {
                continue;
            }

            longestGapSeconds = Math.Max(longestGapSeconds, gap);
            if (gap > maxGapSeconds)
            {
                longGapCount++;
            }
        }

        return (longGapCount, longestGapSeconds);
    }

    private static SceneObjectInfo[] FindMaxConcurrentDominantText(SceneObjectInfo[] dominantTextObjects)
    {
        if (dominantTextObjects.Length <= 1)
        {
            return dominantTextObjects;
        }

        TimeSpan[] sampleTimes = dominantTextObjects
            .SelectMany(item => new[] { item.Element.Start, item.Element.Start + item.Element.Length })
            .Distinct()
            .Order()
            .ToArray();
        SceneObjectInfo[] max = [];
        foreach (TimeSpan time in sampleTimes)
        {
            SceneObjectInfo[] active = dominantTextObjects
                .Where(item => item.Element.Start <= time && time < item.Element.Start + item.Element.Length)
                .ToArray();
            if (active.Length > max.Length)
            {
                max = active;
            }
        }

        return max;
    }

    private static bool IsTextBackingPlate(SceneObjectInfo info)
    {
        if (HasRole(info, "text-backing", "backing", "caption-backing", "label-backing"))
        {
            return true;
        }

        return ContainsAny(info.Element.Name, "text backing", "title backing", "caption backing", "label backing", "backing plate")
               || ContainsAny(info.Object.Name, "text backing", "title backing", "caption backing", "label backing", "backing plate");
    }

    private static bool HasHeavyCardEffects(EngineObject obj)
    {
        if (obj is not Drawable drawable)
        {
            return false;
        }

        FilterEffect? effect = drawable.FilterEffect.CurrentValue;
        int heavyEffects = FlattenEffects(effect).Count(IsHeavyCardEffect);
        return heavyEffects >= 1;
    }

    private static bool IsHeavyCardEffect(FilterEffect effect)
    {
        return effect switch
        {
            DropShadow dropShadow => Math.Max(dropShadow.Sigma.CurrentValue.Width, dropShadow.Sigma.CurrentValue.Height) >= 10
                                     || Math.Abs(dropShadow.Position.CurrentValue.X) + Math.Abs(dropShadow.Position.CurrentValue.Y) >= 16,
            Blur blur => Math.Max(blur.Sigma.CurrentValue.Width, blur.Sigma.CurrentValue.Height) >= 1,
            _ => false
        };
    }

    private static IEnumerable<FilterEffect> FlattenEffects(FilterEffect? effect)
    {
        if (effect is null)
        {
            yield break;
        }

        if (effect is FilterEffectGroup group)
        {
            foreach (FilterEffect child in group.Children)
            {
                foreach (FilterEffect nested in FlattenEffects(child))
                {
                    yield return nested;
                }
            }
        }
        else
        {
            yield return effect;
        }
    }

    private static IEnumerable<Color> ExtractColors(EngineObject obj)
    {
        switch (obj)
        {
            case TextBlock text:
                foreach (Color color in ExtractBrushColors(text.Fill.CurrentValue))
                {
                    yield return color;
                }

                if (text.Pen.CurrentValue?.Brush.CurrentValue is { } textPenBrush)
                {
                    foreach (Color color in ExtractBrushColors(textPenBrush))
                    {
                        yield return color;
                    }
                }

                break;
            case Shape shape:
                foreach (Color color in ExtractBrushColors(shape.Fill.CurrentValue))
                {
                    yield return color;
                }

                if (shape.Pen.CurrentValue?.Brush.CurrentValue is { } shapePenBrush)
                {
                    foreach (Color color in ExtractBrushColors(shapePenBrush))
                    {
                        yield return color;
                    }
                }

                break;
        }

        if (obj is Drawable drawable)
        {
            foreach (FilterEffect effect in FlattenEffects(drawable.FilterEffect.CurrentValue))
            {
                if (effect is DropShadow dropShadow)
                {
                    yield return dropShadow.Color.CurrentValue;
                }
            }
        }
    }

    private static IEnumerable<Color> ExtractBrushColors(Brush? brush)
    {
        switch (brush)
        {
            case SolidColorBrush solid:
                yield return solid.Color.CurrentValue;
                break;
            case GradientBrush gradient:
                foreach (GradientStop stop in gradient.GradientStops)
                {
                    yield return stop.Color.CurrentValue;
                }

                break;
        }
    }

    private static (int ObjectCount, int TransitionCount) AnalyzeGradientFalloff(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        List<QualityIssue> issues)
    {
        List<SceneObjectInfo> hardGradientObjects = [];
        int hardTransitionCount = 0;
        foreach (SceneObjectInfo info in objects)
        {
            if (info.Object is not Shape shape
                || shape.Fill.CurrentValue is not GradientBrush gradient)
            {
                continue;
            }

            int transitions = CountHardGradientTransitions(gradient);
            bool largeAmbientFalloff = IsLargeAmbientGradient(scene, info)
                                       && gradient.GradientStops.Count <= 2
                                       && !HasSoftFalloffEffect(info.Object);
            if (transitions == 0 && !largeAmbientFalloff)
            {
                continue;
            }

            hardGradientObjects.Add(info);
            hardTransitionCount += Math.Max(1, transitions);
        }

        if (hardGradientObjects.Count > 0)
        {
            issues.Add(new QualityIssue(
                "gradientFalloff",
                Advisory,
                "Large ambient gradients have abrupt color or alpha boundaries.",
                $"{hardGradientObjects.Count} gradient-filled shapes have hard stop transitions or two-stop ambient falloff without a softening effect; detected {hardTransitionCount} hard transitions.",
                "Use at least three falloff stops, spread color/alpha transitions over wider offsets, add a real Blur/SKSL texture when appropriate, or replace the shape with procedural surface texture.",
                null,
                hardGradientObjects.Select(item => item.Element.Id.ToString()).ToArray(),
                hardGradientObjects.Select(item => item.Object.Id.ToString()).ToArray()));
        }

        return (hardGradientObjects.Count, hardTransitionCount);
    }

    private static int CountHardGradientTransitions(GradientBrush gradient)
    {
        GradientStop[] stops = gradient.GradientStops
            .OrderBy(stop => stop.Offset.CurrentValue)
            .ToArray();
        int count = 0;
        for (int i = 1; i < stops.Length; i++)
        {
            GradientStop previous = stops[i - 1];
            GradientStop current = stops[i];
            double offsetGap = Math.Max(0.001, current.Offset.CurrentValue - previous.Offset.CurrentValue);
            double lumaDelta = Math.Abs(RelativeLuma(current.Color.CurrentValue) - RelativeLuma(previous.Color.CurrentValue));
            double alphaDelta = Math.Abs(current.Color.CurrentValue.A - previous.Color.CurrentValue.A) / 255d;
            double saturationDelta = Math.Abs(current.Color.CurrentValue.ToHsv().S - previous.Color.CurrentValue.ToHsv().S) / 100d;
            bool abruptOffset = offsetGap <= 0.12
                                && (lumaDelta >= 0.25 || alphaDelta >= 0.35 || saturationDelta >= 0.35);
            if (abruptOffset)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsLargeAmbientGradient(Scene scene, SceneObjectInfo info)
    {
        ObjectBounds bounds = GetBounds(scene, info);
        double sceneArea = Math.Max(1, scene.FrameSize.Width * scene.FrameSize.Height);
        string name = $"{info.Element.Name} {info.Object.Name}";
        return bounds.Width * bounds.Height >= sceneArea * 0.10
               && ContainsAny(name, "ambient", "aperture", "glow", "bloom", "flare", "halo", "light");
    }

    private static bool HasSoftFalloffEffect(EngineObject obj)
    {
        if (obj is not Drawable drawable)
        {
            return false;
        }

        return FlattenEffects(drawable.FilterEffect.CurrentValue).Any(effect =>
            (effect is Blur blur
                && Math.Max(blur.Sigma.CurrentValue.Width, blur.Sigma.CurrentValue.Height) >= 6)
            || effect is SKSLScriptEffect);
    }

    private static int CountAnimatedProperties(EngineObject obj)
    {
        int count = obj.Properties.Count(property => property.Animation is not null);
        foreach (IProperty property in obj.Properties)
        {
            switch (property.CurrentValue)
            {
                case EngineObject nested:
                    count += CountAnimatedProperties(nested);
                    break;
                case ICoreList list:
                    foreach (object? item in list)
                    {
                        if (item is EngineObject nestedItem)
                        {
                            count += CountAnimatedProperties(nestedItem);
                        }
                    }

                    break;
            }
        }

        return count;
    }

    private static int CountHardCutLikeBoundaries(Scene scene)
    {
        TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
        TimeSpan[] boundaries = scene.Children
            .SelectMany(element => new[] { element.Start, element.Start + element.Length })
            .Where(time => time > TimeSpan.Zero && time < duration)
            .Distinct()
            .OrderBy(time => time)
            .ToArray();

        int count = 0;
        foreach (TimeSpan boundary in boundaries)
        {
            bool hasBoundaryChange = scene.Children.Any(element =>
                Math.Abs((element.Start - boundary).TotalSeconds) <= 0.04
                || Math.Abs((element.Start + element.Length - boundary).TotalSeconds) <= 0.04);
            bool hasBridgeAnimation = scene.Children
                .Where(element => element.Range.Contains(boundary - TimeSpan.FromMilliseconds(20))
                                  || element.Range.Contains(boundary + TimeSpan.FromMilliseconds(20)))
                .SelectMany(element => element.Objects)
                .Any(HasBridgeAnimation);
            if (hasBoundaryChange && !hasBridgeAnimation)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasBridgeAnimation(EngineObject obj)
    {
        return obj.Properties.Any(property =>
                   property.Animation is IKeyFrameAnimation animation
                   && animation.KeyFrames.Count >= 2
                   && (string.Equals(property.Name, nameof(Drawable.Opacity), StringComparison.Ordinal)
                       || string.Equals(property.Name, nameof(Drawable.Transform), StringComparison.Ordinal)
                       || string.Equals(property.Name, nameof(TextBlock.Spacing), StringComparison.Ordinal)))
               || obj.Properties.Any(property => property.CurrentValue is EngineObject nested && HasBridgeAnimation(nested));
    }

    private static ObjectBounds GetBounds(Scene scene, SceneObjectInfo info)
    {
        double width;
        double height;
        switch (info.Object)
        {
            case TextBlock text:
                width = EstimateTextWidth(text);
                height = EstimateTextHeight(text);
                break;
            case RectShape rect:
                width = rect.Width.CurrentValue;
                height = rect.Height.CurrentValue;
                break;
            case RoundedRectShape rounded:
                width = rounded.Width.CurrentValue;
                height = rounded.Height.CurrentValue;
                break;
            case EllipseShape ellipse:
                width = ellipse.Width.CurrentValue;
                height = ellipse.Height.CurrentValue;
                break;
            default:
                width = 100;
                height = 100;
                break;
        }

        (double x, double y) = GetTranslate(info.Object);
        double centerX = scene.FrameSize.Width / 2d + x;
        double centerY = scene.FrameSize.Height / 2d + y;
        return new ObjectBounds(centerX, centerY, Math.Max(1, width), Math.Max(1, height));
    }

    private static double EstimateTextWidth(TextBlock text)
    {
        string[] lines = (text.Text.CurrentValue ?? string.Empty).Split('\n');
        int maxCharacters = lines.Length == 0 ? 0 : lines.Max(line => line.Count(ch => !char.IsControl(ch)));
        double glyphAdvance = text.Size.CurrentValue * 0.56 + Math.Max(0, text.Spacing.CurrentValue);
        return Math.Max(text.Size.CurrentValue, maxCharacters * glyphAdvance);
    }

    private static double EstimateTextHeight(TextBlock text)
    {
        int lines = Math.Max(1, (text.Text.CurrentValue ?? string.Empty).Split('\n').Length);
        return Math.Max(text.Size.CurrentValue, lines * text.Size.CurrentValue * 1.18);
    }

    private static (double X, double Y) GetTranslate(EngineObject obj)
    {
        if (obj is not Drawable drawable)
        {
            return (0, 0);
        }

        return FindTranslate(drawable.Transform.CurrentValue);
    }

    private static (double X, double Y) FindTranslate(Transform? transform)
    {
        switch (transform)
        {
            case TranslateTransform translate:
                return (translate.X.CurrentValue, translate.Y.CurrentValue);
            case TransformGroup group:
                double x = 0;
                double y = 0;
                foreach (Transform child in group.Children)
                {
                    (double childX, double childY) = FindTranslate(child);
                    x += childX;
                    y += childY;
                }

                return (x, y);
            default:
                return (0, 0);
        }
    }

    private static bool Overlaps(Element first, Element second)
    {
        TimeSpan firstEnd = first.Start + first.Length;
        TimeSpan secondEnd = second.Start + second.Length;
        return first.Start < secondEnd && second.Start < firstEnd;
    }

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = ax - bx;
        double dy = ay - by;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool ContainsAny(string? value, params string[] tokens)
    {
        return !string.IsNullOrWhiteSpace(value)
               && tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRole(SceneObjectInfo info, params string[] roles)
    {
        return roles.Any(role => HasRole(info.Element.Name, role) || HasRole(info.Object.Name, role));
    }

    private static bool HasRole(string? value, string role)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalizedValue = value.Replace('_', '-');
        string normalizedRole = role.Replace('_', '-');
        return normalizedValue.Contains($"[role:{normalizedRole}]", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.Contains($"role:{normalizedRole}", StringComparison.OrdinalIgnoreCase)
               || normalizedValue.Contains($"role={normalizedRole}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHighTempoProfile(string? styleProfile)
    {
        return ContainsAny(styleProfile, "kinetic", "high-tempo", "fast", "quick", "promo", "1.5", "120", "140")
               || ExtractBpm(styleProfile) is >= 120 and <= 140;
    }

    private static bool HueIn(Color color, double start, double end)
    {
        double hue = color.ToHsv().H;
        return start <= end
            ? hue >= start && hue <= end
            : hue >= start || hue <= end;
    }

    private static double RelativeLuma(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;

    private static string ToHexArgb(Color color)
        => FormattableString.Invariant($"#{color.A:x2}{color.R:x2}{color.G:x2}{color.B:x2}");

    private static string Shorten(string text)
    {
        string normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 48 ? normalized : normalized[..45] + "...";
    }

    private static QualityIssue CreateIssue(
        string category,
        string severity,
        string message,
        string evidence,
        string suggestedFix,
        SceneObjectInfo info,
        string? time)
    {
        return new QualityIssue(
            category,
            severity,
            message,
            evidence,
            suggestedFix,
            time,
            [info.Element.Id.ToString()],
            [info.Object.Id.ToString()]);
    }

    private readonly record struct SceneObjectInfo(Element Element, EngineObject Object);

    private readonly record struct ObjectBounds(double CenterX, double CenterY, double Width, double Height);

    private enum DepthBand
    {
        Background,
        Midground,
        Foreground
    }
}
