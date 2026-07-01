using Beutl.AgentToolkit.Rendering;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class QualityAnalyzerTests
{
    [Test]
    public async Task All_caps_text_is_reported_as_advisory_issue()
    {
        Scene scene = CreateScene();
        AddText(scene, "BREAKING NEWS NOW", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "typography"
                && issue.Severity == "minor"
                && issue.Message.Contains("all-caps", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Metrics.Typography.AllCapsTextCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Mixed_case_text_passes_typography_case_check()
    {
        Scene scene = CreateScene();
        AddText(scene, "Launch notes", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues.Where(issue => issue.Category == "typography"), Is.Empty);
            Assert.That(result.Metrics.Typography.AllCapsTextCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Rect_background_is_allowed_but_rect_dominance_is_advisory()
    {
        Scene backgroundScene = CreateScene();
        AddRect(backgroundScene, "Background plate", zIndex: 0, width: 1920, height: 1080);

        QualityReviewResponse backgroundResult = await AnalyzeAsync(backgroundScene, evaluateMotion: false);

        Scene rectHeavyScene = CreateScene();
        AddRect(rectHeavyScene, "Panel 1", zIndex: 3, width: 420, height: 130, x: -400, y: -220);
        AddRect(rectHeavyScene, "Panel 2", zIndex: 4, width: 360, height: 120, x: 280, y: -40);
        AddRect(rectHeavyScene, "Panel 3", zIndex: 5, width: 300, height: 110, x: -20, y: 180);
        AddText(rectHeavyScene, "Launch notes", zIndex: 10);

        QualityReviewResponse rectHeavyResult = await AnalyzeAsync(rectHeavyScene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(backgroundResult.Issues.Where(issue => issue.Category == "shapeDiversity"), Is.Empty);
            Assert.That(rectHeavyResult.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "shapeDiversity" && issue.Severity == "minor"));
            Assert.That(rectHeavyResult.PassesQualityGate, Is.True);
            Assert.That(rectHeavyResult.Metrics.ShapeDiversity.NonBackgroundRectShapeCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Misaligned_text_background_plate_is_advisory()
    {
        Scene scene = CreateScene();
        AddRoundedRect(scene, "Title backing plate", zIndex: 8, width: 560, height: 150, x: 160, y: 0);
        AddText(scene, "Launch notes", zIndex: 10, x: -120, y: 0);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "textBackgroundFit"
                && issue.Severity == "minor"
                && issue.ElementIds.Count >= 2));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Decorative_rect_without_backing_role_is_not_treated_as_text_plate()
    {
        Scene scene = CreateScene();
        AddRect(scene, "Refractive slice [role:decorative]", zIndex: 8, width: 680, height: 110, x: 160, y: 0);
        AddText(scene, "Launch notes", zIndex: 10, x: -120, y: 0);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.That(result.Issues.Where(issue => issue.Category == "textBackgroundFit"), Is.Empty);
    }

    [Test]
    public async Task High_tempo_profile_restricts_short_lived_copy()
    {
        Scene scene = CreateScene(durationSeconds: 4);
        Element text = AddText(
            scene,
            "Build precise motion quickly now",
            zIndex: 10,
            size: 64);
        text.Length = TimeSpan.FromSeconds(1.5);

        QualityReviewResponse result = await AnalyzeAsync(
            scene,
            evaluateMotion: false,
            styleProfile: "high-tempo-promo");

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "typographyReadTime"
                && issue.Severity == "major"
                && issue.SuggestedFix.Contains("1.5s", StringComparison.Ordinal)));
            Assert.That(result.PassesQualityGate, Is.False);
        });
    }

    [Test]
    public async Task Non_flow_element_with_multiple_objects_is_reported()
    {
        Scene scene = CreateScene();
        Element element = AddText(scene, "Launch notes", zIndex: 10);
        element.AddObject(new EllipseShape { Name = "Extra accent" });

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "elementStructure"
                && issue.Severity == "major"
                && issue.ObjectIds.Count == 2));
            Assert.That(result.PassesQualityGate, Is.False);
            Assert.That(result.Metrics.Structure.NonFlowMultiObjectElementCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Flow_operator_element_with_multiple_objects_is_allowed()
    {
        Scene scene = CreateScene();
        Element element = AddText(scene, "Launch notes", zIndex: 10);
        element.AddObject(new DrawableGroup { Name = "Flow grouping operator" });

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues.Where(issue => issue.Category == "elementStructure"), Is.Empty);
            Assert.That(result.Metrics.Structure.FlowMultiObjectElementCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Large_unclear_foreground_shape_is_advisory()
    {
        Scene scene = CreateScene();
        AddEllipse(scene, "EllipseShape", zIndex: 5, width: 480, height: 320, x: 0, y: 0, color: Color.Parse("#ff6ca8ff"));
        AddText(scene, "Launch notes", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "shapeIntent"
                && issue.Severity == "minor"));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Structure.UnclearForegroundShapeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Animated_shape_without_motion_intent_is_advisory()
    {
        Scene scene = CreateScene(durationSeconds: 2);
        Element element = AddEllipse(
            scene,
            "Soft accent",
            zIndex: 5,
            width: 160,
            height: 120,
            x: 0,
            y: 0,
            color: Color.Parse("#ff6ca8ff"));
        var ellipse = (EllipseShape)element.Objects[0];
        var transform = (TransformGroup)ellipse.Transform.CurrentValue!;
        ((TranslateTransform)transform.Children[0]).X.Animation = CreateFloatAnimation();

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "motionIntent"
                && issue.Severity == "minor"));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Structure.AnimatedShapeWithoutMotionIntentCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Decorative_glint_ellipse_is_advisory_ambiguous()
    {
        Scene scene = CreateScene(durationSeconds: 3);
        Element element = AddEllipse(
            scene,
            "[role:decorative] glass glint ellipse intro sweep",
            zIndex: 8,
            width: 760,
            height: 260,
            x: 0,
            y: 0,
            color: Color.Parse("#88ffcc66"));
        var ellipse = (EllipseShape)element.Objects[0];
        var transform = (TransformGroup)ellipse.Transform.CurrentValue!;
        ((TranslateTransform)transform.Children[0]).X.Animation = CreateFloatAnimation();

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "decorativeShapeClarity"
                && issue.Severity == "minor"
                && issue.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.ShapeDiversity.AmbiguousDecorativeShapeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Large_ambient_two_stop_gradient_without_softening_is_advisory()
    {
        Scene scene = CreateScene(durationSeconds: 3);
        var ellipse = new EllipseShape
        {
            Name = "[role:background] amber ambient aperture drift",
            Width = { CurrentValue = 1120 },
            Height = { CurrentValue = 720 },
            Fill =
            {
                CurrentValue = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#ddffb34d"), 0),
                        new GradientStop(Color.Parse("#00120f0a"), 1)
                    }
                }
            },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(0, 0) }
                }
            }
        };
        AddObject(scene, "[role:background] amber ambient aperture drift element", zIndex: 2, ellipse);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "gradientFalloff"
                && issue.Severity == "minor"
                && issue.Message.Contains("abrupt", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Palette.HardGradientObjectCount, Is.EqualTo(1));
            Assert.That(result.Metrics.Palette.HardGradientTransitionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task High_tempo_profile_flags_sparse_event_density_and_slow_holds_as_advisory()
    {
        Scene scene = CreateScene(durationSeconds: 30);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff20242b"));
        AddText(scene, "Launch notes", zIndex: 10, size: 72);

        QualityReviewResponse result = await AnalyzeAsync(
            scene,
            evaluateMotion: false,
            styleProfile: "high-tempo-promo 130bpm");

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Severity == "minor"
                && issue.Message.Contains("too sparse", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Severity == "minor"
                && issue.Message.Contains("held too long", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Tempo.HighTempoProfile, Is.True);
            Assert.That(result.Metrics.Tempo.TargetBpm, Is.EqualTo(130));
            Assert.That(result.Metrics.Tempo.SlowHoldCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Relaxing_aesthetics_suppresses_high_tempo_hold_advisory_but_keeps_sparse_events()
    {
        Scene scene = CreateScene(durationSeconds: 30);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff20242b"));
        AddText(scene, "Launch notes", zIndex: 10, size: 72);

        QualityReviewResponse relaxed = await AnalyzeAsync(
            scene,
            evaluateMotion: false,
            styleProfile: "high-tempo-promo 130bpm",
            relaxAesthetics: true);

        Assert.Multiple(() =>
        {
            Assert.That(relaxed.Issues, Has.None.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Message.Contains("held too long", StringComparison.OrdinalIgnoreCase)));
            Assert.That(relaxed.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Message.Contains("too sparse", StringComparison.OrdinalIgnoreCase)));
            Assert.That(relaxed.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task High_tempo_profile_reports_sparse_foreground_boundaries_and_long_gaps()
    {
        Scene scene = CreateScene(durationSeconds: 30);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff20242b"));
        Element first = AddText(scene, "Compose", zIndex: 10, size: 72);
        first.Start = TimeSpan.Zero;
        first.Length = TimeSpan.FromSeconds(2);
        Element second = AddText(scene, "Export", zIndex: 10, size: 72);
        second.Start = TimeSpan.FromSeconds(28);
        second.Length = TimeSpan.FromSeconds(2);

        QualityReviewResponse result = await AnalyzeAsync(
            scene,
            evaluateMotion: false,
            styleProfile: "high-tempo-promo 130bpm");

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Message.Contains("Foreground scene changes", StringComparison.Ordinal)));
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Message.Contains("Foreground event gaps", StringComparison.Ordinal)));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Tempo.RequiredTimelineEventsPerSecond, Is.GreaterThan(1.0));
            Assert.That(result.Metrics.Tempo.LongForegroundGapCount, Is.GreaterThan(0));
            Assert.That(result.Metrics.Tempo.LongestForegroundEventGapSeconds, Is.GreaterThan(20));
        });
    }

    [Test]
    public async Task Saturated_dark_teal_cyan_magenta_palette_is_advisory()
    {
        Scene scene = CreateScene();
        AddRect(scene, "Deep teal background", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff020711"));
        AddRoundedRect(scene, "Cyan panel", zIndex: 5, width: 420, height: 120, x: -260, color: Color.Parse("#ff00ffff"));
        AddText(scene, "Launch notes", zIndex: 10, fill: Color.Parse("#ffff00ff"));

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "paletteHarmony" && issue.Severity == "minor"));
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Metrics.Palette.HasDarkTealCyanMagentaPalette, Is.True);
        });
    }

    [Test]
    public async Task Material_ui_card_texture_is_advisory()
    {
        Scene scene = CreateScene();
        AddCard(scene, "Card A", zIndex: 4, x: -260);
        AddCard(scene, "Card B", zIndex: 5, x: 260);
        AddText(scene, "Launch notes", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "materialUiLook" && issue.Severity == "minor"));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Relaxing_aesthetics_suppresses_shape_and_card_advisories()
    {
        Scene scene = CreateScene(durationSeconds: 3);
        Element decorative = AddEllipse(
            scene,
            "[role:decorative] glass glint ellipse intro sweep",
            zIndex: 8,
            width: 760,
            height: 260,
            x: 0,
            y: 0,
            color: Color.Parse("#88ffcc66"));
        var ellipse = (EllipseShape)decorative.Objects[0];
        var transform = (TransformGroup)ellipse.Transform.CurrentValue!;
        ((TranslateTransform)transform.Children[0]).X.Animation = CreateFloatAnimation();
        AddCard(scene, "Card A", zIndex: 4, x: -260);
        AddCard(scene, "Card B", zIndex: 5, x: 260);

        QualityReviewResponse strict = await AnalyzeAsync(scene, evaluateMotion: false);
        QualityReviewResponse relaxed = await AnalyzeAsync(scene, evaluateMotion: false, relaxAesthetics: true);

        Assert.Multiple(() =>
        {
            Assert.That(strict.Issues, Has.Some.Matches<QualityIssue>(issue => issue.Category == "decorativeShapeClarity"));
            Assert.That(strict.Issues, Has.Some.Matches<QualityIssue>(issue => issue.Category == "materialUiLook"));

            Assert.That(relaxed.Issues, Has.None.Matches<QualityIssue>(issue => issue.Category == "decorativeShapeClarity"));
            Assert.That(relaxed.Issues, Has.None.Matches<QualityIssue>(issue => issue.Category == "materialUiLook"));
            Assert.That(relaxed.PassesQualityGate, Is.True);
            Assert.That(relaxed.Metrics.ShapeDiversity.AmbiguousDecorativeShapeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Too_many_dominant_type_elements_are_advisory()
    {
        Scene scene = CreateScene();
        AddText(scene, "Build", zIndex: 10, x: -420, y: -180, size: 96);
        AddText(scene, "Edit", zIndex: 11, x: 120, y: -120, size: 96);
        AddText(scene, "Grade", zIndex: 12, x: -260, y: 80, size: 96);
        AddText(scene, "Export", zIndex: 13, x: 330, y: 160, size: 96);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "visualHierarchy"
                && issue.Severity == "minor"
                && issue.ElementIds.Count == 4));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Sequential_dominant_type_elements_do_not_trigger_visual_hierarchy()
    {
        Scene scene = CreateScene(durationSeconds: 4);
        for (int i = 0; i < 4; i++)
        {
            Element element = AddText(scene, $"Word {i}", zIndex: 10 + i, size: 96);
            element.Start = TimeSpan.FromSeconds(i);
            element.Length = TimeSpan.FromSeconds(0.8);
        }

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.None.Matches<QualityIssue>(issue => issue.Category == "visualHierarchy"));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Named_final_resolve_is_exempt_from_high_tempo_hold_warning()
    {
        Scene scene = CreateScene(durationSeconds: 8);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff20242b"));
        Element beat = AddText(scene, "Build", zIndex: 10, size: 72);
        beat.Start = TimeSpan.Zero;
        beat.Length = TimeSpan.FromSeconds(0.8);
        Element resolve = AddText(scene, "[role:resolve] Final mark", zIndex: 11, size: 72);
        resolve.Start = TimeSpan.FromSeconds(1);
        resolve.Length = TimeSpan.FromSeconds(7);

        QualityReviewResponse result = await AnalyzeAsync(
            scene,
            evaluateMotion: false,
            styleProfile: "high-tempo-promo 130bpm");

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.None.Matches<QualityIssue>(issue =>
                issue.Category == "tempoRhythm"
                && issue.Message.Contains("held too long", StringComparison.OrdinalIgnoreCase)));
            Assert.That(result.Metrics.Tempo.SlowHoldCount, Is.EqualTo(0));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Short_lived_long_copy_is_reported()
    {
        Scene scene = CreateScene(durationSeconds: 4);
        Element text = AddText(
            scene,
            "Design motion graphics faster with precise timeline editing",
            zIndex: 10,
            size: 64);
        text.Length = TimeSpan.FromSeconds(1.2);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "typographyReadTime"
                && issue.Severity == "major"
                && issue.Time == "00:00:00"));
            Assert.That(result.PassesQualityGate, Is.False);
        });
    }

    [Test]
    public async Task Dense_foreground_effect_stacks_are_advisory()
    {
        Scene scene = CreateScene();
        AddEffectStackedEllipse(scene, "Glow shard A", zIndex: 4, x: -360);
        AddEffectStackedEllipse(scene, "Glow shard B", zIndex: 5, x: 0);
        AddEffectStackedEllipse(scene, "Glow shard C", zIndex: 6, x: 360);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "effectIntent"
                && issue.Severity == "minor"
                && issue.ObjectIds.Count == 3));
            Assert.That(result.PassesQualityGate, Is.True);
        });
    }

    [Test]
    public async Task Low_motion_variation_is_included_in_quality_review()
    {
        Scene scene = CreateScene(durationSeconds: 3);
        AddRect(scene, "Static field", zIndex: 0, width: 1920, height: 1080, color: Colors.Black);
        AddText(scene, "Launch notes", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "motionContinuity" && issue.Severity == "major"));
            Assert.That(result.PassesQualityGate, Is.False);
            Assert.That(result.Metrics.MotionContinuity.MotionVerdict, Is.EqualTo("low-motion-variation"));
        });
    }

    [Test]
    public async Task Balanced_scene_passes_quality_gate()
    {
        Scene scene = CreateScene(durationSeconds: 3);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff20242b"));
        AddRoundedRect(scene, "Title backing plate", zIndex: 8, width: 640, height: 150, color: Color.Parse("#eeedf0e8"));
        AddText(scene, "Launch notes", zIndex: 10, fill: Color.Parse("#ff1a2028"));
        AddEllipse(scene, "Soft accent", zIndex: 6, width: 160, height: 160, x: 430, y: -140, color: Color.Parse("#ffb85c38"));

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Issues.Where(issue => issue.Severity is "critical" or "major"), Is.Empty);
        });
    }

    [Test]
    public async Task Distinctive_but_legible_design_passes_gate()
    {
        // Exercises several previously-blocking aesthetics at once: four hero-scale
        // texts, a saturated palette, and a rect-dominant foreground. Each is now
        // advisory, so a legible, structurally valid scene must still pass the gate.
        Scene scene = CreateScene(durationSeconds: 3);
        AddRect(scene, "Background plate", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff101418"));
        AddRect(scene, "Panel 1", zIndex: 3, width: 420, height: 130, x: -400, y: -220, color: Color.Parse("#ffff5a1f"));
        AddRect(scene, "Panel 2", zIndex: 4, width: 360, height: 120, x: 280, y: -40, color: Color.Parse("#ffe11d8f"));
        AddRect(scene, "Panel 3", zIndex: 5, width: 300, height: 110, x: -20, y: 180, color: Color.Parse("#ff1fb6ff"));
        AddText(scene, "Build", zIndex: 10, x: -420, y: -180, size: 96, fill: Color.Parse("#ffffe14d"));
        AddText(scene, "Edit", zIndex: 11, x: 120, y: -120, size: 96, fill: Color.Parse("#ff4dff9e"));
        AddText(scene, "Grade", zIndex: 12, x: -260, y: 80, size: 96, fill: Color.Parse("#ff4dc8ff"));
        AddText(scene, "Export", zIndex: 13, x: 330, y: 160, size: 96, fill: Color.Parse("#ffff7a4d"));

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PassesQualityGate, Is.True);
            Assert.That(result.Issues.Where(issue => issue.Severity is "critical" or "major"), Is.Empty);
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "visualHierarchy" && issue.Severity == "minor"));
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "shapeDiversity" && issue.Severity == "minor"));
        });
    }

    private static ValueTask<QualityReviewResponse> AnalyzeAsync(
        Scene scene,
        bool evaluateMotion = true,
        string? styleProfile = null,
        bool relaxAesthetics = false)
        => new QualityAnalyzer(new MotionVariationAnalyzer(new StillRenderer())).AnalyzeAsync(
            scene,
            timeSeconds: null,
            sampleCount: 3,
            renderScale: 1,
            styleProfile,
            allowAllCaps: false,
            allowHardCuts: false,
            allowRectDominance: false,
            relaxAesthetics: relaxAesthetics,
            evaluateMotion: evaluateMotion,
            cancellationToken: CancellationToken.None);

    private static Scene CreateScene(double durationSeconds = 1)
    {
        return new Scene(1920, 1080, "quality")
        {
            Duration = TimeSpan.FromSeconds(durationSeconds),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), "Scene.scene"))
        };
    }

    private static Element AddText(
        Scene scene,
        string text,
        int zIndex,
        float x = 0,
        float y = 0,
        Color? fill = null,
        float size = 72)
    {
        var block = new TextBlock
        {
            Name = text,
            Text = { CurrentValue = text },
            Size = { CurrentValue = size },
            Fill = { CurrentValue = new SolidColorBrush(fill ?? Colors.White) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, y) }
                }
            }
        };
        return AddObject(scene, $"{text} element", zIndex, block);
    }

    private static Element AddRect(
        Scene scene,
        string name,
        int zIndex,
        float width,
        float height,
        float x = 0,
        float y = 0,
        Color? color = null)
    {
        var rect = new RectShape
        {
            Name = name,
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color ?? Color.Parse("#ff28323c")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, y) }
                }
            }
        };
        return AddObject(scene, $"{name} element", zIndex, rect);
    }

    private static Element AddRoundedRect(
        Scene scene,
        string name,
        int zIndex,
        float width,
        float height,
        float x = 0,
        float y = 0,
        Color? color = null)
    {
        var rect = new RoundedRectShape
        {
            Name = name,
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            CornerRadius = { CurrentValue = new CornerRadius(22) },
            Fill = { CurrentValue = new SolidColorBrush(color ?? Color.Parse("#ffeeeee7")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, y) }
                }
            }
        };
        return AddObject(scene, $"{name} element", zIndex, rect);
    }

    private static Element AddEllipse(
        Scene scene,
        string name,
        int zIndex,
        float width,
        float height,
        float x,
        float y,
        Color color)
    {
        var ellipse = new EllipseShape
        {
            Name = name,
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, y) }
                }
            }
        };
        return AddObject(scene, $"{name} element", zIndex, ellipse);
    }

    private static void AddCard(Scene scene, string name, int zIndex, float x)
    {
        var card = new RoundedRectShape
        {
            Name = name,
            Width = { CurrentValue = 460 },
            Height = { CurrentValue = 260 },
            CornerRadius = { CurrentValue = new CornerRadius(18) },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#fff8f9fb")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, 0) }
                }
            },
            FilterEffect =
            {
                CurrentValue = new FilterEffectGroup
                {
                    Children =
                    {
                        new DropShadow
                        {
                            Position = { CurrentValue = new Point(16, 18) },
                            Sigma = { CurrentValue = new Size(20, 20) },
                            Color = { CurrentValue = Color.Parse("#66000000") }
                        },
                        new Blur
                        {
                            Sigma = { CurrentValue = new Size(1.4f, 1.4f) }
                        }
                    }
                }
            }
        };
        AddObject(scene, $"{name} element", zIndex, card);
    }

    private static void AddEffectStackedEllipse(Scene scene, string name, int zIndex, float x)
    {
        var ellipse = new EllipseShape
        {
            Name = name,
            Width = { CurrentValue = 180 },
            Height = { CurrentValue = 110 },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff6ca8ff")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, 0) }
                }
            },
            FilterEffect =
            {
                CurrentValue = new FilterEffectGroup
                {
                    Children =
                    {
                        new Blur { Sigma = { CurrentValue = new Size(1.2f, 1.2f) } },
                        new Blur { Sigma = { CurrentValue = new Size(1.6f, 1.6f) } },
                        new Blur { Sigma = { CurrentValue = new Size(2.0f, 2.0f) } }
                    }
                }
            }
        };
        AddObject(scene, $"{name} element", zIndex, ellipse);
    }

    private static KeyFrameAnimation<float> CreateFloatAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.Zero,
                Value = 0
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(1),
                Value = 120
            },
            out _);
        return animation;
    }

    private static Element AddObject(Scene scene, string name, int zIndex, EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            ZIndex = zIndex,
            Length = scene.Duration,
            Uri = scene.Uri is null
                ? null
                : new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri.LocalPath)!, $"{Guid.NewGuid():N}.belm"))
        };
        element.AddObject(obj);
        scene.Children.Add(element);
        return element;
    }
}
