using Beutl.AgentToolkit.Design;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Composition;
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
    MotionCraftMetrics MotionCraft,
    MotionContinuityMetrics MotionContinuity,
    TransitionVocabularyMetrics? TransitionVocabulary = null,
    PaletteBalanceMetrics? PaletteBalance = null);

public sealed record TypographyMetrics(
    int TextObjectCount,
    int AllCapsTextCount,
    int ExcessiveSpacingCount,
    int TextPlateMismatchCount,
    int LowContrastTextCount,
    IReadOnlyList<TypographyContrastMetric> Contrast);

public sealed record TypographyContrastMetric(
    string ElementId,
    string ObjectId,
    string Text,
    double WorstContrastRatio,
    string WorstTime,
    bool DecorativeIntent,
    IReadOnlyList<TypographyContrastSample> Samples);

public sealed record TypographyContrastSample(
    string Time,
    double ContrastRatio);

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

public sealed record MotionCraftMetrics(
    EasingDiversityMetrics EasingDiversity,
    MotionUniformityMetrics MotionUniformity,
    MotionArcMetrics MotionArc);

public sealed record EasingDiversityMetrics(
    int AnimatedTransitionCount,
    int LinearTransitionCount,
    double LinearTransitionShare);

public sealed record MotionUniformityMetrics(
    int AnimatedElementCount,
    int LargestClusterElementCount,
    double LargestClusterShare,
    double? LargestClusterStartSeconds,
    double? LargestClusterDurationSeconds,
    string? LargestClusterDirection);

public sealed record MotionArcMetrics(
    bool Evaluated,
    int DominantAnimatedElementCount,
    bool HasAnticipation,
    bool HasSettle,
    double HoldSeconds);

public sealed record TransitionVocabularyMetrics(
    IReadOnlyDictionary<string, int> Histogram,
    IReadOnlyList<TransitionBoundaryClassification> Boundaries);

public sealed record TransitionBoundaryClassification(
    double TimeSeconds,
    string Type,
    string Evidence,
    IReadOnlyList<string> ElementIds);

public sealed record PaletteRoleColor(
    string Role,
    string Color);

public sealed record PaletteBalanceMetrics(
    int SampledPixelCount,
    IReadOnlyList<PaletteRoleShare> RoleShares,
    double NeutralShare,
    bool AccentFlooded,
    bool DominantBelowFloor);

public sealed record PaletteRoleShare(
    string Role,
    string Color,
    double Share);

public sealed class QualityAnalyzer(MotionVariationAnalyzer motionVariationAnalyzer, StillRenderer stillRenderer)
{
    private const string Critical = "critical";
    private const string Major = "major";
    private const string Minor = "minor";
    private const double TypographyContrastFloor = 3.0;
    private const double TextColorDistanceThreshold = 36;
    private const int TextBackgroundSamplePaddingPixels = 4;

    // Advisory issues never fail the quality gate; only Critical/Major do. Aesthetic
    // opinions use this so they surface as guidance without blocking export.
    private const string Advisory = Minor;

    // Gate policy: typographyReadTime and rendered typographyContrast, elementStructure,
    // and motionContinuity are the standing deterministic blockers. layerDensity can
    // also block only when a motion-graphics caller supplies a quantitative plan and
    // authored foreground density falls below half of that plan; palette/background
    // aesthetics remain advisory.

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
        string? videoType = null,
        IReadOnlyList<double>? beatTimesSeconds = null,
        IReadOnlyList<PaletteRoleColor>? paletteRoleColors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);

        VideoTypeProfile? videoProfile = string.IsNullOrWhiteSpace(videoType)
            ? null
            : VideoTypeCatalog.Resolve(videoType);
        VideoTypeGateProfile gateProfile = videoProfile?.GateProfile ?? VideoTypeGateProfile.None;

        // relaxAesthetics only suppresses non-blocking aesthetic/pacing advisories; the
        // blocking checks (read time, element structure, motion) still run regardless.
        bool relaxRectDominance = allowRectDominance || relaxAesthetics;
        bool relaxHardCuts = allowHardCuts || relaxAesthetics;
        bool resolvedAllowStillness = allowStillness || gateProfile.ImpliedAllowStillness;
        bool resolvedAllowMinimalDensity = allowMinimalDensity || gateProfile.ImpliedAllowMinimalDensity;
        string? tempoStyleProfile = ResolveTempoStyleProfile(styleProfile, gateProfile);

        SceneObjectInfo[] objects = EnumerateObjects(scene).ToArray();
        List<QualityIssue> issues = [];
        TypographyMetrics typography = AnalyzeTypography(objects, allowAllCaps, issues);
        ShapeDiversityMetrics shapeDiversity = AnalyzeShapeDiversity(scene, objects, relaxRectDominance, relaxAesthetics, issues);
        StructureMetrics structure = AnalyzeStructure(scene, objects, allowMultiObjectElements, issues);
        int textPlateMismatchCount = AnalyzeTextBackgroundFit(scene, objects, issues);
        typography = typography with { TextPlateMismatchCount = textPlateMismatchCount };
        PaletteMetrics palette = AnalyzePalette(scene, objects, allowMonochrome, issues);
        BackgroundRichnessMetrics backgroundRichness = AnalyzeBackgroundRichness(
            scene,
            objects,
            gateProfile.SuppressBackgroundRichness,
            issues);
        AnalyzeMaterialUiLook(objects, relaxAesthetics, issues);
        AnalyzeDesignStructure(
            scene,
            objects,
            styleProfile,
            allowDenseText,
            gateProfile.SuppressCaptionRoleHierarchy,
            issues);
        TempoMetrics tempo = AnalyzeTempo(scene, objects, tempoStyleProfile, relaxAesthetics, issues);
        AnalyzeAudioSync(scene, beatTimesSeconds, issues);
        TransitionVocabularyMetrics? transitionVocabulary = AnalyzeTransitionVocabulary(
            scene,
            objects,
            gateProfile,
            relaxHardCuts,
            relaxAesthetics,
            issues);
        LayerDensityMetrics layerDensity = AnalyzeLayerDensity(
            scene,
            objects,
            styleProfile,
            resolvedAllowMinimalDensity,
            gateProfile.ForceMotionGraphicsIntentOff,
            gateProfile.SuppressLayerDensityPlanGate,
            plannedForegroundElementsPerShot,
            issues);
        AnalyzeTimelineCoverage(scene, objects, gateProfile.RunTimelineCoverage, issues);
        MotionCraftMetrics motionCraft = AnalyzeMotionCraft(scene, objects, gateProfile, issues);
        IReadOnlyList<RenderedFrameAnalysis>? renderedFrames = null;
        PaletteBalanceMetrics? paletteBalance = null;
        try
        {
            if (evaluateMotion)
            {
                IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
                renderedFrames = await RenderAnalysisFramesAsync(
                    scene,
                    sampleTimes,
                    renderScale,
                    cancellationToken).ConfigureAwait(false);
                IReadOnlyList<TypographyContrastMetric> contrast = AnalyzeRenderedTypographyContrast(
                    scene,
                    objects,
                    renderedFrames,
                    allowMonochrome,
                    issues);
                typography = typography with
                {
                    LowContrastTextCount = contrast.Count(item => item.WorstContrastRatio < TypographyContrastFloor),
                    Contrast = contrast
                };
                paletteBalance = AnalyzePaletteBalance(
                    renderedFrames,
                    paletteRoleColors,
                    gateProfile,
                    issues);
            }

            MotionContinuityMetrics motion = await AnalyzeMotionAsync(
                scene,
                objects,
                timeSeconds,
                sampleCount,
                renderScale,
                renderedFrames,
                relaxHardCuts,
                relaxAesthetics,
                resolvedAllowStillness,
                evaluateMotion,
                gateProfile.SuppressCutRhythm,
                gateProfile.RewordCutRhythmForTransitions,
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

            if (videoProfile is not null)
            {
                notes.Add($"Video type: {videoProfile.Name} (implied: {string.Join("; ", gateProfile.DescribeAdjustments())}).");
            }

            return new QualityReviewResponse(
                !hasBlockingIssue,
                verdict,
                issues,
                new QualityMetrics(typography, shapeDiversity, palette, structure, tempo, backgroundRichness, layerDensity, motionCraft, motion, transitionVocabulary, paletteBalance),
                notes);
        }
        finally
        {
            if (renderedFrames is not null)
            {
                foreach (RenderedFrameAnalysis frame in renderedFrames)
                {
                    frame.Dispose();
                }
            }
        }
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

        return new TypographyMetrics(textCount, allCapsCount, spacingCount, 0, 0, []);
    }

    private static void AnalyzeDesignStructure(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        string? styleProfile,
        bool allowDenseText,
        bool suppressCaptionRoleHierarchy,
        List<QualityIssue> issues)
    {
        bool highTempoProfile = IsHighTempoProfile(styleProfile);
        SceneObjectInfo[] textObjects = objects.Where(item => item.Object is TextBlock).ToArray();
        SceneObjectInfo[] dominantTextObjects = textObjects
            .Where(item => item.Object is TextBlock textBlock && textBlock.Size.CurrentValue >= 88)
            .ToArray();

        SceneObjectInfo[] concurrentDominantTextObjects = FindMaxConcurrentDominantText(dominantTextObjects);
        if (concurrentDominantTextObjects.Length >= 4
            && (!suppressCaptionRoleHierarchy || !concurrentDominantTextObjects.All(IsCaptionRoleText)))
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

    private static Rect? GetGeometryLocalBounds(GeometryShape shape)
    {
        if (shape.Data.CurrentValue is null)
        {
            return null;
        }

        using var resource = (GeometryShape.Resource)shape.ToResource(CompositionContext.Default);
        return resource.Data?.Bounds;
    }

    private static bool HasCompensatingTranslate(Drawable drawable, Rect geometryBounds)
    {
        Matrix matrix = drawable.Transform.CurrentValue?.CreateMatrix(CompositionContext.Default) ?? Matrix.Identity;
        if (!matrix.TryDecomposeTransform(out Vector translate, out _, out _, out _))
        {
            return false;
        }

        return Math.Abs(translate.X + geometryBounds.X) <= 1
               && Math.Abs(translate.Y + geometryBounds.Y) <= 1;
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

        var offsetGeometryShapes = new List<(SceneObjectInfo Info, Rect Bounds)>();
        foreach (SceneObjectInfo item in objects)
        {
            if (item.Object is not GeometryShape geometryShape)
            {
                continue;
            }

            if (GetGeometryLocalBounds(geometryShape) is not { } geometryBounds
                || (Math.Abs(geometryBounds.X) <= 0.5f && Math.Abs(geometryBounds.Y) <= 0.5f)
                || HasCompensatingTranslate(geometryShape, geometryBounds))
            {
                continue;
            }

            offsetGeometryShapes.Add((item, geometryBounds));
        }

        if (offsetGeometryShapes.Count > 0)
        {
            string offsets = string.Join("; ", offsetGeometryShapes
                .Take(3)
                .Select(item => $"'{item.Info.Object.Name}' path bounds start at ({item.Bounds.X:0.##}, {item.Bounds.Y:0.##})"));
            issues.Add(new QualityIssue(
                "geometryPathOffset",
                Advisory,
                "GeometryShape paths whose bounds do not start at (0, 0) render offset from their aligned position.",
                $"{offsetGeometryShapes.Count} GeometryShape objects have non-zero path-bounds origins without a compensating translate: {offsets}. The drawn center lands at the alignment-resolved center plus the path bounds origin.",
                "Author PathGeometry coordinates so the artwork's top-left is at (0, 0) (all coordinates non-negative), or add TranslateTransform(-boundsX, -boundsY) as a static transform. Paths centered on (0, 0) shift up-left by half their size.",
                null,
                offsetGeometryShapes.Select(item => item.Info.Element.Id.ToString()).ToArray(),
                offsetGeometryShapes.Select(item => item.Info.Object.Id.ToString()).ToArray()));
        }

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
        bool suppressIssue,
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
        if (!suppressIssue && fullFrameBackgrounds.Length == 1 && flatSingleLayerBackgrounds.Length == 1)
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
        bool forceMotionGraphicsIntentOff,
        bool suppressPlanGate,
        double plannedForegroundElementsPerShot,
        List<QualityIssue> issues)
    {
        bool motionGraphicsIntent = !forceMotionGraphicsIntentOff
                                    && HasMotionGraphicsIntent(scene, objects, styleProfile);
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
        bool densityPlanViolation = motionGraphicsIntent && !suppressPlanGate && bandsBelowHalfPlan > 0;
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

    private static void AnalyzeTimelineCoverage(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        bool enabled,
        List<QualityIssue> issues)
    {
        if (!enabled)
        {
            return;
        }

        double durationSeconds = Math.Max(0, scene.Duration.TotalSeconds);
        if (durationSeconds <= 0)
        {
            return;
        }

        (double Start, double End)[] intervals = scene.Children
            .Where(element => element.Length > TimeSpan.Zero)
            .Where(element => objects.Any(info => info.Element == element && IsVisibleTimelineObject(info.Object)))
            .Select(element => (
                Start: Math.Clamp(element.Start.TotalSeconds, 0, durationSeconds),
                End: Math.Clamp((element.Start + element.Length).TotalSeconds, 0, durationSeconds)))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ThenBy(interval => interval.End)
            .ToArray();

        var gaps = new List<(double Start, double End)>();
        double cursor = 0;
        foreach ((double start, double end) in intervals)
        {
            if (start - cursor > 0.5)
            {
                gaps.Add((cursor, start));
            }

            cursor = Math.Max(cursor, end);
        }

        if (durationSeconds - cursor > 0.5)
        {
            gaps.Add((cursor, durationSeconds));
        }

        if (gaps.Count == 0)
        {
            return;
        }

        string ranges = string.Join(
            ", ",
            gaps.Select(gap => $"{FormatSeconds(gap.Start)}-{FormatSeconds(gap.End)}"));
        issues.Add(new QualityIssue(
            "timelineCoverage",
            Advisory,
            "The timeline has visible-element gaps longer than 0.5 seconds.",
            $"Empty visible ranges: {ranges}.",
            "Close unintended blank ranges by extending adjacent clips/photos, adding a visible transition plate, or recording the silence/black gap as intentional.",
            null,
            [],
            []));
    }

    private static void AnalyzeAudioSync(
        Scene scene,
        IReadOnlyList<double>? beatTimesSeconds,
        List<QualityIssue> issues)
    {
        double[] beats = beatTimesSeconds?
            .Where(double.IsFinite)
            .Where(seconds => seconds >= 0)
            .Distinct()
            .Order()
            .ToArray() ?? [];
        if (beats.Length == 0)
        {
            return;
        }

        CutBoundary[] lateBoundaries = FindInteriorCutBoundaries(scene)
            .Select(boundary =>
            {
                double boundarySeconds = boundary.Time.TotalSeconds;
                double nearestBeat = beats.MinBy(beat => Math.Abs(beat - boundarySeconds));
                double distance = Math.Abs(nearestBeat - boundarySeconds);
                return boundary with
                {
                    NearestBeatSeconds = nearestBeat,
                    NearestBeatDistanceSeconds = distance
                };
            })
            .Where(boundary => boundary.NearestBeatDistanceSeconds is >= 0.04 and <= 0.12)
            .ToArray();
        if (lateBoundaries.Length == 0)
        {
            return;
        }

        string evidence = string.Join(
            "; ",
            lateBoundaries.Select(boundary =>
                $"{FormatSeconds(boundary.Time.TotalSeconds)} is {boundary.NearestBeatDistanceSeconds * 1000:F0} ms from beat {FormatSeconds(boundary.NearestBeatSeconds)}"));
        issues.Add(new QualityIssue(
            "audioSync",
            Advisory,
            "Visible cut boundaries land just off the supplied beat grid.",
            evidence,
            "Snap visible Element starts/ends or accent keyframes onto nearby beatTimesSeconds, or move them more than 120 ms away when an intentionally off-grid cut is desired.",
            null,
            lateBoundaries
                .SelectMany(boundary => boundary.Elements)
                .Select(element => element.Id.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            []));
    }

    private static TransitionVocabularyMetrics? AnalyzeTransitionVocabulary(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        VideoTypeGateProfile gateProfile,
        bool allowHardCuts,
        bool relaxAesthetics,
        List<QualityIssue> issues)
    {
        if (!gateProfile.RunTransitionVocabulary)
        {
            return null;
        }

        TransitionBoundaryClassification[] boundaries = FindInteriorCutBoundaries(scene)
            .Select(boundary => ClassifyTransitionBoundary(scene, objects, boundary))
            .ToArray();
        IReadOnlyDictionary<string, int> histogram = boundaries
            .GroupBy(boundary => boundary.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        if (boundaries.Length > 0
            && !allowHardCuts
            && !relaxAesthetics
            && HasInconsistentTransitionVocabulary(histogram))
        {
            string evidence = string.Join(", ", histogram.Select(item => $"{item.Key}:{item.Value}"));
            issues.Add(new QualityIssue(
                "transitionVocabulary",
                Advisory,
                "The edit mixes transition types without a clear majority vocabulary.",
                $"Transition histogram: {evidence}.",
                "Consolidate to one continuity vocabulary, such as repeated dissolves for time passage, a directional sweep for location/topic changes, or documented hard cuts where clean cuts are intentional.",
                null,
                boundaries
                    .SelectMany(boundary => boundary.ElementIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                []));
        }

        return new TransitionVocabularyMetrics(histogram, boundaries);
    }

    private static TransitionBoundaryClassification ClassifyTransitionBoundary(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        CutBoundary boundary)
    {
        TimeSpan pre = boundary.Time - TimeSpan.FromMilliseconds(20);
        TimeSpan post = boundary.Time + TimeSpan.FromMilliseconds(20);
        SceneObjectInfo[] activeBefore = objects
            .Where(info => IsElementActiveAt(info.Element, pre))
            .ToArray();
        SceneObjectInfo[] activeAfter = objects
            .Where(info => IsElementActiveAt(info.Element, post))
            .ToArray();
        SceneObjectInfo[] bridgeObjects = objects
            .Where(info => IsElementActiveAt(info.Element, pre) && IsElementActiveAt(info.Element, post))
            .ToArray();

        SceneObjectInfo[] outgoingOpacity = activeBefore
            .Where(info => HasOpacityTrend(info, boundary.Time, decreasing: true))
            .ToArray();
        SceneObjectInfo[] incomingOpacity = activeAfter
            .Where(info => HasOpacityTrend(info, boundary.Time, decreasing: false))
            .ToArray();
        if (outgoingOpacity.Length > 0 && incomingOpacity.Length > 0)
        {
            return CreateTransitionClassification(
                boundary,
                "dissolve",
                "Opacity ramps overlap across the boundary.",
                outgoingOpacity.Concat(incomingOpacity));
        }

        SceneObjectInfo[] dipPlates = bridgeObjects
            .Where(info => IsFullFrameBackground(scene, info))
            .Where(info => HasOpacityPeakAtBoundary(info, boundary.Time))
            .ToArray();
        if (dipPlates.Length > 0)
        {
            return CreateTransitionClassification(
                boundary,
                "dip-to-color",
                "A full-frame plate opacity peaks at the boundary.",
                dipPlates);
        }

        SceneObjectInfo[] sweepBridges = bridgeObjects
            .Where(HasTransformAnimation)
            .ToArray();
        if (sweepBridges.Length > 0)
        {
            return CreateTransitionClassification(
                boundary,
                "sweep",
                "A transform-animated element remains visible on both sides of the boundary.",
                sweepBridges);
        }

        return CreateTransitionClassification(
            boundary,
            "hard-cut",
            "No overlap dissolve, transform bridge, or dip plate was detected.",
            boundary.Elements.SelectMany(element => objects.Where(info => info.Element == element)));
    }

    private static TransitionBoundaryClassification CreateTransitionClassification(
        CutBoundary boundary,
        string type,
        string evidence,
        IEnumerable<SceneObjectInfo> objects)
    {
        string[] elementIds = objects
            .Select(info => info.Element.Id.ToString())
            .Concat(boundary.Elements.Select(element => element.Id.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new TransitionBoundaryClassification(
            Math.Round(boundary.Time.TotalSeconds, 4, MidpointRounding.AwayFromZero),
            type,
            evidence,
            elementIds);
    }

    private static bool HasInconsistentTransitionVocabulary(IReadOnlyDictionary<string, int> histogram)
    {
        int nonHardTypes = histogram
            .Where(item => !string.Equals(item.Key, "hard-cut", StringComparison.Ordinal))
            .Count(item => item.Value > 0);
        if (nonHardTypes > 2)
        {
            return true;
        }

        int total = histogram.Values.Sum();
        if (total < 3
            || !histogram.TryGetValue("hard-cut", out int hardCuts)
            || hardCuts == 0)
        {
            return false;
        }

        int styledCuts = total - hardCuts;
        if (styledCuts == 0)
        {
            return false;
        }

        int majority = histogram.Values.Max();
        return majority <= total / 2d;
    }

    private static bool HasOpacityTrend(SceneObjectInfo info, TimeSpan boundary, bool decreasing)
    {
        foreach (AnimatedPropertyInfo animated in EnumerateAnimatedProperties(info))
        {
            if (!string.Equals(animated.Property.Name, nameof(Drawable.Opacity), StringComparison.Ordinal)
                || !TryGetAnimatedFloatTrend(animated.Animation, info.Element, boundary, out double delta))
            {
                continue;
            }

            if (decreasing ? delta < -5 : delta > 5)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOpacityPeakAtBoundary(SceneObjectInfo info, TimeSpan boundary)
    {
        foreach (AnimatedPropertyInfo animated in EnumerateAnimatedProperties(info))
        {
            if (!string.Equals(animated.Property.Name, nameof(Drawable.Opacity), StringComparison.Ordinal)
                || !TryGetAnimationTime(animated.Animation, info.Element, boundary, out TimeSpan animationTime))
            {
                continue;
            }

            IKeyFrame[] keyFrames = animated.Animation.KeyFrames
                .OrderBy(item => item.KeyTime)
                .ToArray();
            if (keyFrames.Length < 3)
            {
                continue;
            }

            int nearestIndex = Enumerable.Range(0, keyFrames.Length)
                .OrderBy(index => Math.Abs((keyFrames[index].KeyTime - animationTime).TotalSeconds))
                .First();
            IKeyFrame nearest = keyFrames[nearestIndex];
            if (Math.Abs((nearest.KeyTime - animationTime).TotalSeconds) > 0.16
                || !TryGetFrameDoubleValue(nearest, out double peak)
                || peak < 70)
            {
                continue;
            }

            double previous = nearestIndex > 0 && TryGetFrameDoubleValue(keyFrames[nearestIndex - 1], out double prev)
                ? prev
                : peak;
            double next = nearestIndex + 1 < keyFrames.Length && TryGetFrameDoubleValue(keyFrames[nearestIndex + 1], out double nxt)
                ? nxt
                : peak;
            if (previous <= peak - 20 && next <= peak - 20)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTransformAnimation(SceneObjectInfo info)
    {
        return EnumerateAnimatedProperties(info)
            .Any(animated => animated.PropertyPath.Contains("Transform", StringComparison.Ordinal)
                             || animated.PropertyPath.Contains("Translate", StringComparison.Ordinal)
                             || animated.PropertyPath.Contains("Rotation", StringComparison.Ordinal)
                             || animated.PropertyPath.Contains("Scale", StringComparison.Ordinal)
                             || animated.PropertyPath.Contains("Skew", StringComparison.Ordinal));
    }

    private static bool TryGetAnimatedFloatTrend(
        IKeyFrameAnimation animation,
        Element element,
        TimeSpan boundary,
        out double delta)
    {
        delta = 0;
        if (!TryGetAnimationTime(animation, element, boundary, out TimeSpan animationTime))
        {
            return false;
        }

        IKeyFrame[] keyFrames = animation.KeyFrames
            .OrderBy(item => item.KeyTime)
            .ToArray();
        if (keyFrames.Length < 2)
        {
            return false;
        }

        IKeyFrame previous = keyFrames[0];
        IKeyFrame next = keyFrames[1];
        for (int i = 1; i < keyFrames.Length; i++)
        {
            if (animationTime <= keyFrames[i].KeyTime)
            {
                previous = keyFrames[i - 1];
                next = keyFrames[i];
                break;
            }

            previous = keyFrames[i - 1];
            next = keyFrames[i];
        }

        if (!TryGetFrameDoubleValue(previous, out double previousValue)
            || !TryGetFrameDoubleValue(next, out double nextValue)
            || next.KeyTime <= previous.KeyTime)
        {
            return false;
        }

        delta = nextValue - previousValue;
        return true;
    }

    private static bool TryGetAnimationTime(
        IKeyFrameAnimation animation,
        Element element,
        TimeSpan boundary,
        out TimeSpan animationTime)
    {
        bool useGlobalClock = animation is KeyFrameAnimation keyFrameAnimation && keyFrameAnimation.UseGlobalClock;
        animationTime = useGlobalClock ? boundary : boundary - element.Start;
        return animationTime >= TimeSpan.Zero;
    }

    private static bool TryGetFrameDoubleValue(IKeyFrame frame, out double value)
    {
        value = 0;
        switch (frame.Value)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue:
                value = doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool IsElementActiveAt(Element element, TimeSpan time)
        => time >= TimeSpan.Zero
           && element.IsEnabled
           && element.Length > TimeSpan.Zero
           && element.Range.Contains(time);

    private static PaletteBalanceMetrics? AnalyzePaletteBalance(
        IReadOnlyList<RenderedFrameAnalysis> frames,
        IReadOnlyList<PaletteRoleColor>? paletteRoleColors,
        VideoTypeGateProfile gateProfile,
        List<QualityIssue> issues)
    {
        if (gateProfile.SuppressPaletteBalance || paletteRoleColors is not { Count: > 0 } || frames.Count == 0)
        {
            return null;
        }

        PaletteRoleColorSpec[] roles = paletteRoleColors
            .Select(TryCreateRoleColorSpec)
            .OfType<PaletteRoleColorSpec>()
            .ToArray();
        if (roles.Length == 0)
        {
            return null;
        }

        long[] counts = new long[roles.Length];
        long neutralCount = 0;
        long sampledPixels = 0;
        foreach (RenderedFrameAnalysis frame in frames)
        {
            using Bitmap bitmap = frame.Bitmap.Convert(
                BitmapColorType.Bgra8888,
                BitmapAlphaType.Unpremul,
                BitmapColorSpace.Srgb);
            sampledPixels += AccumulatePaletteBalance(bitmap, roles, counts, ref neutralCount);
        }

        if (sampledPixels == 0)
        {
            return new PaletteBalanceMetrics(0, [], 0, false, false);
        }

        PaletteRoleShare[] shares = roles
            .Select((role, index) => new PaletteRoleShare(
                role.Role,
                role.Color,
                Math.Round(counts[index] / (double)sampledPixels, 4, MidpointRounding.AwayFromZero)))
            .ToArray();
        double neutralShare = Math.Round(neutralCount / (double)sampledPixels, 4, MidpointRounding.AwayFromZero);
        double accentShare = shares
            .Where(share => IsAccentRole(share.Role))
            .Sum(share => share.Share);
        double dominantShare = shares
            .Where(share => IsDominantBackgroundRole(share.Role))
            .Sum(share => share.Share);
        bool accentFlooded = accentShare > 0.20;
        bool dominantBelowFloor = shares.Any(share => IsDominantBackgroundRole(share.Role)) && dominantShare < 0.40;

        if (accentFlooded || dominantBelowFloor)
        {
            issues.Add(new QualityIssue(
                "paletteBalance",
                Advisory,
                "Rendered palette role areas drift away from a stable 60-30-10 balance.",
                $"Accent share {accentShare:P0}; dominant/background share {dominantShare:P0}; neutral share {neutralShare:P0}.",
                "Use the 60-30-10 rule and Itten's contrast of extension: keep accent color near a small emphasis role, restore the bg-base/background dominant area above about 40%, and move large support areas into secondary or neutral roles.",
                null,
                [],
                []));
        }

        return new PaletteBalanceMetrics(
            (int)Math.Min(int.MaxValue, sampledPixels),
            shares,
            neutralShare,
            accentFlooded,
            dominantBelowFloor);
    }

    private static int AccumulatePaletteBalance(
        Bitmap bitmap,
        IReadOnlyList<PaletteRoleColorSpec> roles,
        long[] counts,
        ref long neutralCount)
    {
        int stride = Math.Max(1, (int)Math.Sqrt(bitmap.Width * bitmap.Height / 20000d));
        int sampled = 0;
        for (int y = 0; y < bitmap.Height; y += stride)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x += stride)
            {
                if (!TryReadRgb(row, x, bitmap.BytesPerPixel, bitmap.ColorType, out byte r, out byte g, out byte b, out byte a)
                    || a < 8)
                {
                    continue;
                }

                sampled++;
                if (IsNearNeutral(r, g, b))
                {
                    neutralCount++;
                }

                int nearestIndex = 0;
                int nearestDistance = int.MaxValue;
                for (int i = 0; i < roles.Count; i++)
                {
                    int distance = ColorDistanceSquared(r, g, b, roles[i].R, roles[i].G, roles[i].B);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = i;
                    }
                }

                counts[nearestIndex]++;
            }
        }

        return sampled;
    }

    private static bool TryReadRgb(
        Span<byte> row,
        int x,
        int bytesPerPixel,
        BitmapColorType colorType,
        out byte r,
        out byte g,
        out byte b,
        out byte a)
    {
        r = 0;
        g = 0;
        b = 0;
        a = 0;
        int offset = x * bytesPerPixel;
        if (offset < 0 || offset + bytesPerPixel > row.Length)
        {
            return false;
        }

        switch (colorType)
        {
            case BitmapColorType.Bgra8888:
                b = row[offset];
                g = row[offset + 1];
                r = row[offset + 2];
                a = row[offset + 3];
                return true;
            case BitmapColorType.Rgba8888:
            case BitmapColorType.Srgba8888:
                r = row[offset];
                g = row[offset + 1];
                b = row[offset + 2];
                a = row[offset + 3];
                return true;
            case BitmapColorType.Rgb888x:
                r = row[offset];
                g = row[offset + 1];
                b = row[offset + 2];
                a = 255;
                return true;
            default:
                return false;
        }
    }

    private static PaletteRoleColorSpec? TryCreateRoleColorSpec(PaletteRoleColor role)
    {
        if (string.IsNullOrWhiteSpace(role.Role)
            || !TryParseRgbHex(role.Color, out byte r, out byte g, out byte b))
        {
            return null;
        }

        return new PaletteRoleColorSpec(role.Role.Trim(), NormalizeHexColor(r, g, b), r, g, b);
    }

    private static bool TryParseRgbHex(string? value, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string hex = value.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        if (hex.Length != 6)
        {
            return false;
        }

        return byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out r)
               && byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)
               && byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    private static string NormalizeHexColor(byte r, byte g, byte b)
        => $"#{r:x2}{g:x2}{b:x2}";

    private static bool IsNearNeutral(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        return max - min <= 14;
    }

    private static int ColorDistanceSquared(byte ar, byte ag, byte ab, byte br, byte bg, byte bb)
    {
        int dr = ar - br;
        int dg = ag - bg;
        int db = ab - bb;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private static bool IsAccentRole(string role)
    {
        string normalized = NormalizeRoleName(role);
        return string.Equals(normalized, "accent", StringComparison.Ordinal)
               || string.Equals(normalized, "roleaccent", StringComparison.Ordinal);
    }

    private static bool IsDominantBackgroundRole(string role)
    {
        string normalized = NormalizeRoleName(role);
        return string.Equals(normalized, "bgbase", StringComparison.Ordinal)
               || normalized.Contains("background", StringComparison.Ordinal)
               || normalized.Contains("dominant", StringComparison.Ordinal);
    }

    private static string NormalizeRoleName(string role)
        => new(role
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static MotionCraftMetrics AnalyzeMotionCraft(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        VideoTypeGateProfile gateProfile,
        List<QualityIssue> issues)
    {
        EasingDiversityMetrics easingDiversity = AnalyzeEasingDiversity(scene, objects, issues);
        MotionUniformityMetrics motionUniformity = AnalyzeMotionUniformity(scene, objects, issues);
        MotionArcMetrics motionArc = AnalyzeLogoIntroMotionArc(
            scene,
            objects,
            gateProfile.RunLogoIntroMotionArc,
            issues);
        return new MotionCraftMetrics(easingDiversity, motionUniformity, motionArc);
    }

    private static EasingDiversityMetrics AnalyzeEasingDiversity(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        List<QualityIssue> issues)
    {
        AnimatedPropertyInfo[] animatedProperties = objects
            .Where(item => IsVisibleForegroundObject(scene, item))
            .SelectMany(EnumerateAnimatedProperties)
            .ToArray();
        KeyFrameTransitionInfo[] transitions = animatedProperties
            .SelectMany(CreateKeyFrameTransitions)
            .ToArray();
        int transitionCount = transitions.Length;
        int linearCount = transitions.Count(item => IsLinearEasing(item.Easing));
        double linearShare = transitionCount == 0 ? 0 : linearCount / (double)transitionCount;

        if (transitionCount >= 6 && linearShare >= 0.90)
        {
            issues.Add(new QualityIssue(
                "easingDiversity",
                Advisory,
                "Most animated keyframe transitions use linear easing, which makes motion read mechanical.",
                $"{linearCount}/{transitionCount} keyframe transitions are linear ({linearShare:P0}).",
                "Mix slow-in/slow-out easing from Disney's 12 principles: use cubic or quintic ease-out for entrances, ease-in-out for moves, and reserve linear easing for intentional mechanical or constant-speed motion.",
                null,
                transitions
                    .Select(item => item.Info.Element.Id.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                transitions
                    .Select(item => item.Info.Object.Id.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
        }

        return new EasingDiversityMetrics(
            transitionCount,
            linearCount,
            Math.Round(linearShare, 4, MidpointRounding.AwayFromZero));
    }

    private static MotionUniformityMetrics AnalyzeMotionUniformity(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        List<QualityIssue> issues)
    {
        Dictionary<Element, SceneObjectInfo[]> foregroundByElement = objects
            .Where(item => IsVisibleForegroundObject(scene, item))
            .GroupBy(item => item.Element)
            .ToDictionary(group => group.Key, group => group.ToArray());
        AnimatedElementMotion[] animatedElements = scene.Children
            .Select(element => AnalyzeElementMotion(element, foregroundByElement))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        if (animatedElements.Length == 0)
        {
            return new MotionUniformityMetrics(0, 0, 0, null, null, null);
        }

        var largestCluster = animatedElements
            .GroupBy(item => item.ClusterKey)
            .Select(group => new
            {
                Key = group.Key,
                Items = group.ToArray()
            })
            .OrderByDescending(group => group.Items.Length)
            .ThenBy(group => group.Key.StartBucket)
            .ThenBy(group => group.Key.DurationBucket)
            .ThenBy(group => group.Key.Direction, StringComparer.Ordinal)
            .First();
        int largestCount = largestCluster.Items.Length;
        double largestShare = largestCount / (double)animatedElements.Length;

        if (largestCount >= 3 && largestShare >= 0.80)
        {
            issues.Add(new QualityIssue(
                "motionUniformity",
                Advisory,
                "Most animated elements start, last, and move in the same way, reducing follow-through and overlapping action.",
                $"{largestCount}/{animatedElements.Length} animated elements cluster at start {largestCluster.Key.StartBucket:F1}s, duration {largestCluster.Key.DurationBucket:F1}s, direction {largestCluster.Key.Direction}.",
                "Stagger starts by 0.1-0.3 seconds and vary durations or translate directions so follow-through and overlapping action are visible.",
                null,
                largestCluster.Items
                    .Select(item => item.Element.Id.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                largestCluster.Items
                    .SelectMany(item => item.ObjectIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
        }

        return new MotionUniformityMetrics(
            animatedElements.Length,
            largestCount,
            Math.Round(largestShare, 4, MidpointRounding.AwayFromZero),
            largestCluster.Key.StartBucket,
            largestCluster.Key.DurationBucket,
            largestCluster.Key.Direction);
    }

    private static MotionArcMetrics AnalyzeLogoIntroMotionArc(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        bool enabled,
        List<QualityIssue> issues)
    {
        if (!enabled)
        {
            return new MotionArcMetrics(false, 0, false, false, 0);
        }

        MotionArcSegment[] allSegments = objects
            .Where(item => IsVisibleForegroundObject(scene, item))
            .SelectMany(CreateMotionArcSegments)
            .OrderBy(item => item.StartSeconds)
            .ThenBy(item => item.EndSeconds)
            .ToArray();
        MotionArcSegment[] movingSegments = allSegments
            .Where(item => item.Magnitude > 0.01)
            .ToArray();
        if (movingSegments.Length == 0)
        {
            return new MotionArcMetrics(true, 0, false, false, 0);
        }

        MotionArcSegment largest = movingSegments.MaxBy(item => item.Magnitude)!;
        MotionArcSegment[] dominantSegments = allSegments
            .Where(item => item.Info.Element == largest.Info.Element && item.Info.Object == largest.Info.Object)
            .OrderBy(item => item.StartSeconds)
            .ThenBy(item => item.EndSeconds)
            .ToArray();
        MotionArcSegment[] dominantMovingSegments = dominantSegments
            .Where(item => item.Magnitude > 0.01)
            .ToArray();
        int dominantElementCount = movingSegments
            .Where(item => item.Magnitude >= largest.Magnitude * 0.80)
            .Select(item => item.Info.Element)
            .Distinct()
            .Count();

        bool hasAnticipation = dominantSegments.Any(segment =>
            segment.EndSeconds <= largest.StartSeconds + 0.001
            && segment.DurationSeconds >= 0.10
            && (segment.Magnitude <= largest.Magnitude * 0.55
                || IsCounterMove(segment, largest)));

        MotionArcSegment finalSegment = dominantMovingSegments
            .OrderBy(item => item.EndSeconds)
            .Last();
        double sceneEnd = scene.Duration > TimeSpan.Zero ? scene.Duration.TotalSeconds : finalSegment.EndSeconds;
        double elementEnd = finalSegment.Info.Element.Length > TimeSpan.Zero
            ? (finalSegment.Info.Element.Start + finalSegment.Info.Element.Length).TotalSeconds
            : sceneEnd;
        double holdEnd = Math.Min(sceneEnd, elementEnd);
        double holdSeconds = Math.Max(0, holdEnd - finalSegment.EndSeconds);
        bool hasSettle = holdSeconds >= 1.0
                         && (IsEaseOutLike(finalSegment.Easing)
                             || HasOvershootReturn(dominantMovingSegments, finalSegment));

        if (!hasAnticipation)
        {
            issues.Add(CreateMotionArcIssue(
                "The logo-intro motion arc is missing a detectable anticipation phase before the main reveal.",
                $"Largest {largest.PropertyPath} change runs {FormatSeconds(largest.StartSeconds)}-{FormatSeconds(largest.EndSeconds)} with no earlier smaller counter-move or hold segment.",
                "Add a brief anticipation before the reveal: a smaller counter-move, compression, opacity breath, or held pre-beat before the largest transform/opacity change.",
                largest));
        }

        if (!hasSettle)
        {
            issues.Add(CreateMotionArcIssue(
                "The logo-intro motion arc is missing a clear settle into the final hold.",
                $"Final {finalSegment.PropertyPath} segment ends at {FormatSeconds(finalSegment.EndSeconds)} with {holdSeconds:F2}s of hold and easing {finalSegment.Easing.GetType().Name}.",
                "End with a non-linear ease-out or overshoot-then-return settle, then keep the final keyframe stable for at least 1 second before the element or scene ends.",
                finalSegment));
        }

        return new MotionArcMetrics(
            true,
            dominantElementCount,
            hasAnticipation,
            hasSettle,
            Math.Round(holdSeconds, 3, MidpointRounding.AwayFromZero));
    }

    private static QualityIssue CreateMotionArcIssue(
        string message,
        string evidence,
        string suggestedFix,
        MotionArcSegment segment)
    {
        return new QualityIssue(
            "motionArc",
            Advisory,
            message,
            evidence,
            suggestedFix,
            null,
            [segment.Info.Element.Id.ToString()],
            [segment.Info.Object.Id.ToString()]);
    }

    private static AnimatedElementMotion? AnalyzeElementMotion(
        Element element,
        IReadOnlyDictionary<Element, SceneObjectInfo[]> foregroundByElement)
    {
        if (!foregroundByElement.TryGetValue(element, out SceneObjectInfo[]? objects)
            || objects.Length == 0
            || !objects.Any(item => HasTransformOrOpacityAnimation(item.Object)))
        {
            return null;
        }

        double deltaX = 0;
        double deltaY = 0;
        bool hasTranslate = false;
        foreach (SceneObjectInfo info in objects)
        {
            if (info.Object is Drawable drawable)
            {
                (double x, double y, bool found) = GetAnimatedTranslateDelta(drawable.Transform.CurrentValue);
                deltaX += x;
                deltaY += y;
                hasTranslate |= found;
            }
        }

        string direction = hasTranslate && Distance(0, 0, deltaX, deltaY) >= 1
            ? BucketDirection(deltaX, deltaY)
            : "none";
        double start = RoundToTenth(element.Start.TotalSeconds);
        double duration = RoundToTenth(element.Length.TotalSeconds);
        return new AnimatedElementMotion(
            element,
            objects.Select(item => item.Object.Id.ToString()).ToArray(),
            new MotionUniformityClusterKey(start, duration, direction));
    }

    private static bool IsVisibleForegroundObject(Scene scene, SceneObjectInfo info)
    {
        return info.Object is not PortalObject
               && (info.Object is not Drawable drawable
                   || drawable.Opacity.CurrentValue > 0.01
                   || drawable.Opacity.Animation is not null)
               && !IsBackgroundObject(scene, info);
    }

    private static IEnumerable<AnimatedPropertyInfo> EnumerateAnimatedProperties(SceneObjectInfo info)
    {
        var visited = new HashSet<EngineObject>();
        foreach (AnimatedPropertyInfo animated in EnumerateAnimatedProperties(info, info.Object, info.Object.GetType().Name, visited))
        {
            yield return animated;
        }
    }

    private static IEnumerable<AnimatedPropertyInfo> EnumerateAnimatedProperties(
        SceneObjectInfo info,
        EngineObject obj,
        string path,
        HashSet<EngineObject> visited)
    {
        if (!visited.Add(obj))
        {
            yield break;
        }

        foreach (IProperty property in obj.Properties)
        {
            string propertyPath = $"{path}.{property.Name}";
            if (property.Animation is IKeyFrameAnimation animation)
            {
                yield return new AnimatedPropertyInfo(info, property, animation, propertyPath);
            }

            foreach (EngineObject nested in EnumerateNestedEngineObjects(property))
            {
                foreach (AnimatedPropertyInfo animated in EnumerateAnimatedProperties(
                             info,
                             nested,
                             $"{propertyPath}.{nested.GetType().Name}",
                             visited))
                {
                    yield return animated;
                }
            }
        }
    }

    private static IEnumerable<EngineObject> EnumerateNestedEngineObjects(IProperty property)
    {
        if (property.CurrentValue is EngineObject nested)
        {
            yield return nested;
        }

        if (property.CurrentValue is ICoreList currentList)
        {
            foreach (object? item in currentList)
            {
                if (item is EngineObject nestedItem)
                {
                    yield return nestedItem;
                }
            }
        }

        if (property is ICoreList propertyList)
        {
            foreach (object? item in propertyList)
            {
                if (item is EngineObject nestedItem)
                {
                    yield return nestedItem;
                }
            }
        }
    }

    private static IEnumerable<KeyFrameTransitionInfo> CreateKeyFrameTransitions(AnimatedPropertyInfo animated)
    {
        IKeyFrame[] keyFrames = animated.Animation.KeyFrames
            .OrderBy(item => item.KeyTime)
            .ToArray();
        for (int i = 1; i < keyFrames.Length; i++)
        {
            IKeyFrame previous = keyFrames[i - 1];
            IKeyFrame next = keyFrames[i];
            if (next.KeyTime <= previous.KeyTime)
            {
                continue;
            }

            yield return new KeyFrameTransitionInfo(animated.Info, animated.PropertyPath, next.Easing);
        }
    }

    private static bool HasTransformOrOpacityAnimation(EngineObject obj)
    {
        if (obj is Drawable drawable)
        {
            if (drawable.Opacity.Animation is IKeyFrameAnimation { KeyFrames.Count: >= 2 }
                || drawable.Transform.Animation is not null
                || HasAnimatedTransform(drawable.Transform.CurrentValue))
            {
                return true;
            }
        }

        foreach (IProperty property in obj.Properties)
        {
            foreach (EngineObject nested in EnumerateNestedEngineObjects(property))
            {
                if (HasTransformOrOpacityAnimation(nested))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnimatedTransform(Transform? transform)
    {
        if (transform is null)
        {
            return false;
        }

        foreach (IProperty property in transform.Properties)
        {
            if (property.Animation is not null)
            {
                return true;
            }

            foreach (EngineObject nested in EnumerateNestedEngineObjects(property))
            {
                if (nested is Transform nestedTransform && HasAnimatedTransform(nestedTransform))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (double X, double Y, bool HasTranslate) GetAnimatedTranslateDelta(Transform? transform)
    {
        switch (transform)
        {
            case TranslateTransform translate:
                {
                    (double x, bool hasX) = GetFloatAnimationDelta(translate.X);
                    (double y, bool hasY) = GetFloatAnimationDelta(translate.Y);
                    return (x, y, hasX || hasY);
                }
            case TransformGroup group:
                {
                    double x = 0;
                    double y = 0;
                    bool found = false;
                    foreach (Transform child in group.Children)
                    {
                        (double childX, double childY, bool childFound) = GetAnimatedTranslateDelta(child);
                        x += childX;
                        y += childY;
                        found |= childFound;
                    }

                    return (x, y, found);
                }
            default:
                return (0, 0, false);
        }
    }

    private static (double Delta, bool HasAnimation) GetFloatAnimationDelta(IProperty<float> property)
    {
        if (property.Animation is not IKeyFrameAnimation animation)
        {
            return (0, false);
        }

        IKeyFrame[] keyFrames = animation.KeyFrames
            .OrderBy(item => item.KeyTime)
            .ToArray();
        if (keyFrames.Length < 2
            || !TryGetDouble(keyFrames[0].Value, out double first)
            || !TryGetDouble(keyFrames[^1].Value, out double last))
        {
            return (0, false);
        }

        return (last - first, true);
    }

    private static IEnumerable<MotionArcSegment> CreateMotionArcSegments(SceneObjectInfo info)
    {
        if (info.Object is Drawable drawable)
        {
            foreach (MotionArcSegment segment in CreateNumericMotionSegments(
                         info,
                         drawable.Opacity,
                         $"{drawable.GetType().Name}.{nameof(Drawable.Opacity)}"))
            {
                yield return segment;
            }

            foreach (MotionArcSegment segment in CreateTranslateMotionSegments(
                         info,
                         drawable.Transform.CurrentValue,
                         $"{drawable.GetType().Name}.{nameof(Drawable.Transform)}"))
            {
                yield return segment;
            }
        }

        foreach (IProperty property in info.Object.Properties)
        {
            foreach (EngineObject nested in EnumerateNestedEngineObjects(property))
            {
                foreach (MotionArcSegment segment in CreateMotionArcSegments(new SceneObjectInfo(info.Element, nested)))
                {
                    yield return segment;
                }
            }
        }
    }

    private static IEnumerable<MotionArcSegment> CreateTranslateMotionSegments(
        SceneObjectInfo info,
        Transform? transform,
        string path)
    {
        switch (transform)
        {
            case TranslateTransform translate:
                foreach (MotionArcSegment segment in CreateNumericMotionSegments(info, translate.X, $"{path}.X"))
                {
                    yield return segment;
                }

                foreach (MotionArcSegment segment in CreateNumericMotionSegments(info, translate.Y, $"{path}.Y"))
                {
                    yield return segment;
                }

                break;
            case TransformGroup group:
                for (int i = 0; i < group.Children.Count; i++)
                {
                    foreach (MotionArcSegment segment in CreateTranslateMotionSegments(
                                 info,
                                 group.Children[i],
                                 $"{path}.Children[{i}]"))
                    {
                        yield return segment;
                    }
                }

                break;
        }
    }

    private static IEnumerable<MotionArcSegment> CreateNumericMotionSegments(
        SceneObjectInfo info,
        IProperty<float> property,
        string path)
    {
        if (property.Animation is not IKeyFrameAnimation animation)
        {
            yield break;
        }

        IKeyFrame[] keyFrames = animation.KeyFrames
            .OrderBy(item => item.KeyTime)
            .ToArray();
        for (int i = 1; i < keyFrames.Length; i++)
        {
            IKeyFrame previous = keyFrames[i - 1];
            IKeyFrame next = keyFrames[i];
            if (next.KeyTime <= previous.KeyTime
                || !TryGetDouble(previous.Value, out double previousValue)
                || !TryGetDouble(next.Value, out double nextValue))
            {
                continue;
            }

            double start = ResolveKeyFrameTime(info.Element, animation, previous).TotalSeconds;
            double end = ResolveKeyFrameTime(info.Element, animation, next).TotalSeconds;
            if (end <= start)
            {
                continue;
            }

            yield return new MotionArcSegment(
                info,
                path,
                start,
                end,
                previousValue,
                nextValue,
                next.Easing);
        }
    }

    private static TimeSpan ResolveKeyFrameTime(Element element, IKeyFrameAnimation animation, IKeyFrame keyFrame)
        => animation.UseGlobalClock ? keyFrame.KeyTime : element.Start + keyFrame.KeyTime;

    private static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case float floatValue when float.IsFinite(floatValue):
                result = floatValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                result = doubleValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool IsLinearEasing(Easing easing) => easing is LinearEasing;

    private static bool IsEaseOutLike(Easing easing)
    {
        if (easing is LinearEasing or HoldEasing)
        {
            return false;
        }

        string typeName = easing.GetType().Name;
        return typeName.Contains("EaseOut", StringComparison.Ordinal)
               || typeName.Contains("EaseInOut", StringComparison.Ordinal);
    }

    private static bool IsCounterMove(MotionArcSegment segment, MotionArcSegment largest)
    {
        if (segment.Magnitude <= 0.01 || largest.Magnitude <= 0.01)
        {
            return false;
        }

        return Math.Sign(segment.Delta) != Math.Sign(largest.Delta)
               && segment.Magnitude <= largest.Magnitude * 0.75;
    }

    private static bool HasOvershootReturn(
        IReadOnlyList<MotionArcSegment> movingSegments,
        MotionArcSegment finalSegment)
    {
        MotionArcSegment? previous = movingSegments
            .Where(item => item.EndSeconds <= finalSegment.StartSeconds + 0.001)
            .OrderBy(item => item.EndSeconds)
            .LastOrDefault();
        return previous is not null
               && Math.Sign(previous.Delta) != Math.Sign(finalSegment.Delta);
    }

    private static string BucketDirection(double x, double y)
    {
        string[] buckets = ["E", "SE", "S", "SW", "W", "NW", "N", "NE"];
        double angle = Math.Atan2(y, x);
        int index = (int)Math.Round(angle / (Math.PI / 4), MidpointRounding.AwayFromZero);
        index = ((index % 8) + 8) % 8;
        return buckets[index];
    }

    private static double RoundToTenth(double value)
        => Math.Round(value * 10d, MidpointRounding.AwayFromZero) / 10d;

    private async ValueTask<IReadOnlyList<RenderedFrameAnalysis>> RenderAnalysisFramesAsync(
        Scene scene,
        IReadOnlyList<TimeSpan> sampleTimes,
        float renderScale,
        CancellationToken cancellationToken)
    {
        var frames = new List<RenderedFrameAnalysis>(sampleTimes.Count);
        try
        {
            foreach (TimeSpan time in sampleTimes)
            {
                frames.Add(await stillRenderer.RenderFrameAnalysisAsync(
                    scene,
                    time,
                    renderScale,
                    cancellationToken).ConfigureAwait(false));
            }

            return frames;
        }
        catch
        {
            foreach (RenderedFrameAnalysis frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
    }

    private static IReadOnlyList<TypographyContrastMetric> AnalyzeRenderedTypographyContrast(
        Scene scene,
        IReadOnlyList<SceneObjectInfo> objects,
        IReadOnlyList<RenderedFrameAnalysis> frames,
        bool allowMonochrome,
        List<QualityIssue> issues)
    {
        var objectInfo = objects
            .Where(item => item.Object is TextBlock)
            .ToDictionary(item => item.Object, item => item);
        var samplesByText = new Dictionary<TextBlock, List<TypographyContrastSample>>();

        foreach (RenderedFrameAnalysis frame in frames)
        {
            using Bitmap bitmap = frame.Bitmap.Convert(
                BitmapColorType.Bgra8888,
                BitmapAlphaType.Unpremul,
                BitmapColorSpace.Srgb);
            foreach (RenderedTextBounds textBounds in frame.TextBounds)
            {
                if (!objectInfo.ContainsKey(textBounds.TextBlock)
                    || !TryGetTextFillColor(textBounds.TextBlock, out Color fill)
                    || fill.A == 0)
                {
                    continue;
                }

                double? contrast = MeasureTextContrast(
                    scene,
                    bitmap,
                    textBounds.Bounds,
                    fill);
                if (contrast is null)
                {
                    continue;
                }

                if (!samplesByText.TryGetValue(textBounds.TextBlock, out List<TypographyContrastSample>? samples))
                {
                    samples = [];
                    samplesByText[textBounds.TextBlock] = samples;
                }

                samples.Add(new TypographyContrastSample(
                    frame.Time.ToString("c"),
                    Math.Round(contrast.Value, 2, MidpointRounding.AwayFromZero)));
            }
        }

        var metrics = new List<TypographyContrastMetric>(samplesByText.Count);
        foreach ((TextBlock textBlock, List<TypographyContrastSample> samples) in samplesByText)
        {
            if (samples.Count == 0 || !objectInfo.TryGetValue(textBlock, out SceneObjectInfo info))
            {
                continue;
            }

            TypographyContrastSample worst = samples.MinBy(sample => sample.ContrastRatio)!;
            bool decorativeIntent = allowMonochrome || HasRole(info, "decorative");
            var metric = new TypographyContrastMetric(
                info.Element.Id.ToString(),
                textBlock.Id.ToString(),
                Shorten(textBlock.Text.CurrentValue ?? string.Empty),
                worst.ContrastRatio,
                worst.Time,
                decorativeIntent,
                samples);
            metrics.Add(metric);

            if (worst.ContrastRatio >= TypographyContrastFloor)
            {
                continue;
            }

            issues.Add(CreateIssue(
                "typographyContrast",
                IntentSeverity(decorativeIntent),
                "Rendered text contrast falls below the 3.0:1 large-text floor.",
                $"Text '{metric.Text}' measured worst contrast {worst.ContrastRatio:F2}:1 at {worst.Time}.",
                decorativeIntent
                    ? "Decorative or monochrome text contrast is allowed; confirm the text is not carrying required information."
                    : "Increase text/background luma separation, add or align a named [role:text-backing] plate, or move the text away from the low-contrast background region.",
                info,
                worst.Time));
        }

        return metrics;
    }

    private static double? MeasureTextContrast(
        Scene scene,
        Bitmap bitmap,
        Rect logicalBounds,
        Color textFill)
    {
        if (!TryResolvePixelBounds(scene, bitmap, logicalBounds, out int left, out int top, out int right, out int bottom))
        {
            return null;
        }

        double maxDistance = 0;
        int opaquePixels = 0;
        for (int y = top; y < bottom; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = left; x < right; x++)
            {
                int offset = x * bitmap.BytesPerPixel;
                byte b = row[offset];
                byte g = row[offset + 1];
                byte r = row[offset + 2];
                byte a = bitmap.BytesPerPixel > 3 ? row[offset + 3] : byte.MaxValue;
                if (a == 0)
                {
                    continue;
                }

                maxDistance = Math.Max(maxDistance, ColorDistance(r, g, b, textFill));
                opaquePixels++;
            }
        }

        if (opaquePixels == 0)
        {
            return null;
        }

        if (maxDistance <= TextColorDistanceThreshold)
        {
            return 1;
        }

        double textLuminance = WcagRelativeLuminance(textFill.R, textFill.G, textFill.B);
        double minContrast = double.PositiveInfinity;
        int sampledPixels = 0;
        double backgroundDistanceThreshold = Math.Max(TextColorDistanceThreshold, maxDistance * 0.60);
        for (int y = top; y < bottom; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = left; x < right; x++)
            {
                int offset = x * bitmap.BytesPerPixel;
                byte b = row[offset];
                byte g = row[offset + 1];
                byte r = row[offset + 2];
                byte a = bitmap.BytesPerPixel > 3 ? row[offset + 3] : byte.MaxValue;
                if (a == 0 || ColorDistance(r, g, b, textFill) < backgroundDistanceThreshold)
                {
                    continue;
                }

                minContrast = Math.Min(minContrast, ContrastRatio(textLuminance, WcagRelativeLuminance(r, g, b)));
                sampledPixels++;
            }
        }

        return sampledPixels == 0 || double.IsPositiveInfinity(minContrast)
            ? 1
            : minContrast;
    }

    private static bool TryResolvePixelBounds(
        Scene scene,
        Bitmap bitmap,
        Rect logicalBounds,
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        double scaleX = scene.FrameSize.Width <= 0 ? 1 : bitmap.Width / (double)scene.FrameSize.Width;
        double scaleY = scene.FrameSize.Height <= 0 ? 1 : bitmap.Height / (double)scene.FrameSize.Height;
        left = Math.Clamp((int)Math.Floor(logicalBounds.Left * scaleX) - TextBackgroundSamplePaddingPixels, 0, bitmap.Width);
        top = Math.Clamp((int)Math.Floor(logicalBounds.Top * scaleY) - TextBackgroundSamplePaddingPixels, 0, bitmap.Height);
        right = Math.Clamp((int)Math.Ceiling(logicalBounds.Right * scaleX) + TextBackgroundSamplePaddingPixels, 0, bitmap.Width);
        bottom = Math.Clamp((int)Math.Ceiling(logicalBounds.Bottom * scaleY) + TextBackgroundSamplePaddingPixels, 0, bitmap.Height);
        return right > left && bottom > top;
    }

    private static bool TryGetTextFillColor(TextBlock textBlock, out Color color)
    {
        if (textBlock.Fill.CurrentValue is SolidColorBrush solid)
        {
            color = solid.Color.CurrentValue;
            return true;
        }

        color = default;
        return false;
    }

    private static double ColorDistance(byte r, byte g, byte b, Color textFill)
    {
        int dr = r - textFill.R;
        int dg = g - textFill.G;
        int db = b - textFill.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static double ContrastRatio(double firstLuminance, double secondLuminance)
    {
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double WcagRelativeLuminance(byte r, byte g, byte b)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(r)) + (0.7152 * Linearize(g)) + (0.0722 * Linearize(b));
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
        IReadOnlyList<RenderedFrameAnalysis>? renderedFrames,
        bool allowHardCuts,
        bool relaxShortSegments,
        bool allowStillness,
        bool evaluateMotion,
        bool suppressCutRhythm,
        bool rewordCutRhythmForTransitions,
        List<QualityIssue> issues,
        CancellationToken cancellationToken)
    {
        int animatedPropertyCount = objects.Sum(item => CountAnimatedProperties(item.Object));
        int shortSegmentCount = scene.Children.Count(element =>
            element.Length > TimeSpan.Zero && element.Length < TimeSpan.FromSeconds(0.45));
        int hardCutCount = CountHardCutLikeBoundaries(scene);
        if (!suppressCutRhythm && !allowHardCuts && hardCutCount >= 2)
        {
            issues.Add(new QualityIssue(
                "cutRhythm",
                Advisory,
                rewordCutRhythmForTransitions
                    ? "Slideshow transitions repeat hard boundaries without enough consistency or bridge motion."
                    : "The timeline has repeated hard-cut-like element boundaries without enough bridging animation.",
                $"{hardCutCount} interior boundaries start or end visible elements with no detected opacity/transform animation.",
                rewordCutRhythmForTransitions
                    ? "Use a consistent transition vocabulary with short overlaps, opacity ramps, transform continuation, or a documented clean-cut photo change."
                    : "Bridge cuts with short overlap, opacity animation, transform continuation, or an intentional rhythm note in the brief.",
                null,
                scene.Children.Select(element => element.Id.ToString()).ToArray(),
                []));
        }

        if (!suppressCutRhythm && !relaxShortSegments && shortSegmentCount >= 3)
        {
            issues.Add(new QualityIssue(
                "cutRhythm",
                Advisory,
                rewordCutRhythmForTransitions
                    ? "Several slideshow segments are very short, which can make transition timing feel inconsistent."
                    : "Several timeline segments are very short, which can produce a chopped-up edit.",
                $"{shortSegmentCount} elements are shorter than 0.45 seconds.",
                rewordCutRhythmForTransitions
                    ? "Lengthen outlier photo segments or align them to the chosen transition vocabulary."
                    : "Group micro-beats into a clearer phrase, or add visible transition continuity between them.",
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

        MotionVariationResponse motion;
        if (renderedFrames is { Count: >= 2 })
        {
            motion = MotionVariationAnalyzer.AnalyzeFrames(
                renderedFrames.Select(frame => (frame.Time, frame.Bitmap)).ToArray(),
                0.02,
                48,
                0.35,
                0.90,
                24);
        }
        else
        {
            IReadOnlyList<TimeSpan> sampleTimes = ResolveSampleTimes(scene, timeSeconds, sampleCount);
            motion = await motionVariationAnalyzer.AnalyzeAsync(
                scene,
                sampleTimes,
                renderScale,
                0.02,
                48,
                0.35,
                0.90,
                24,
                cancellationToken).ConfigureAwait(false);
        }

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

    private static bool IsVisibleTimelineObject(EngineObject obj)
    {
        return obj is not PortalObject
               && (obj is not Drawable drawable || drawable.Opacity.CurrentValue > 0.01);
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

    private static bool IsCaptionRoleText(SceneObjectInfo info)
    {
        return info.Object is TextBlock
               && (HasRole(info, "caption", "captions", "subtitle", "subtitles", "lyric", "lyrics", "line", "echo", "secondary", "credit")
                   || ContainsAny(info.Element.Name, "caption", "subtitle", "lyric", "line", "echo", "secondary", "credit")
                   || ContainsAny(info.Object.Name, "caption", "subtitle", "lyric", "line", "echo", "secondary", "credit"));
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
        int count = 0;
        foreach (CutBoundary cutBoundary in FindInteriorCutBoundaries(scene))
        {
            TimeSpan boundary = cutBoundary.Time;
            bool hasBridgeAnimation = scene.Children
                .Where(element => element.Range.Contains(boundary - TimeSpan.FromMilliseconds(20))
                                  || element.Range.Contains(boundary + TimeSpan.FromMilliseconds(20)))
                .SelectMany(element => element.Objects)
                .Any(HasBridgeAnimation);
            if (!hasBridgeAnimation)
            {
                count++;
            }
        }

        return count;
    }

    private static CutBoundary[] FindInteriorCutBoundaries(Scene scene)
    {
        TimeSpan duration = scene.Duration > TimeSpan.Zero ? scene.Duration : TimeSpan.FromSeconds(1);
        var boundaries = new SortedDictionary<long, List<Element>>();
        foreach (Element element in scene.Children)
        {
            if (!element.IsEnabled
                || element.Length <= TimeSpan.Zero
                || !element.Objects.Any(IsVisibleTimelineObject))
            {
                continue;
            }

            AddBoundary(element.Start, element);
            AddBoundary(element.Start + element.Length, element);
        }

        return boundaries
            .Select(item => new CutBoundary(
                TimeSpan.FromTicks(item.Key),
                item.Value,
                0,
                0))
            .ToArray();

        void AddBoundary(TimeSpan time, Element element)
        {
            if (time <= TimeSpan.Zero || time >= duration)
            {
                return;
            }

            if (!boundaries.TryGetValue(time.Ticks, out List<Element>? elements))
            {
                elements = [];
                boundaries[time.Ticks] = elements;
            }

            elements.Add(element);
        }
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

    private static bool IsExplicitHighTempoProfile(string? styleProfile)
    {
        return ContainsAny(styleProfile, "kinetic", "high-tempo", "fast", "quick", "1.5", "120", "140", "bpm")
               || ExtractBpm(styleProfile) is >= 120 and <= 140;
    }

    private static string? ResolveTempoStyleProfile(string? styleProfile, VideoTypeGateProfile gateProfile)
    {
        if (gateProfile.SuppressTempoAnalysis)
        {
            return null;
        }

        if (gateProfile.SuppressTempoUnlessExplicitHighTempo
            && !IsExplicitHighTempoProfile(styleProfile))
        {
            return null;
        }

        return styleProfile;
    }

    private static string FormatSeconds(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString("c");

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

    private sealed record AnimatedPropertyInfo(
        SceneObjectInfo Info,
        IProperty Property,
        IKeyFrameAnimation Animation,
        string PropertyPath);

    private sealed record KeyFrameTransitionInfo(
        SceneObjectInfo Info,
        string PropertyPath,
        Easing Easing);

    private sealed record AnimatedElementMotion(
        Element Element,
        IReadOnlyList<string> ObjectIds,
        MotionUniformityClusterKey ClusterKey);

    private readonly record struct MotionUniformityClusterKey(
        double StartBucket,
        double DurationBucket,
        string Direction);

    private sealed record MotionArcSegment(
        SceneObjectInfo Info,
        string PropertyPath,
        double StartSeconds,
        double EndSeconds,
        double StartValue,
        double EndValue,
        Easing Easing)
    {
        public double Delta => EndValue - StartValue;

        public double Magnitude => Math.Abs(Delta);

        public double DurationSeconds => EndSeconds - StartSeconds;
    }

    private readonly record struct SceneObjectInfo(Element Element, EngineObject Object);

    private readonly record struct ObjectBounds(double CenterX, double CenterY, double Width, double Height);

    private sealed record PaletteRoleColorSpec(
        string Role,
        string Color,
        byte R,
        byte G,
        byte B);

    private readonly record struct CutBoundary(
        TimeSpan Time,
        IReadOnlyList<Element> Elements,
        double NearestBeatSeconds,
        double NearestBeatDistanceSeconds);

    private enum DepthBand
    {
        Background,
        Midground,
        Foreground
    }
}
