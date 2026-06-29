using Beutl.AgentToolkit.Rendering;
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
    public async Task All_caps_text_is_reported_as_major_issue()
    {
        Scene scene = CreateScene();
        AddText(scene, "BREAKING NEWS NOW", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PassesQualityGate, Is.False);
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "typography"
                && issue.Severity == "major"
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
    public async Task Rect_background_is_allowed_but_rect_dominance_is_reported()
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
                issue.Category == "shapeDiversity" && issue.Severity == "major"));
            Assert.That(rectHeavyResult.Metrics.ShapeDiversity.NonBackgroundRectShapeCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Misaligned_text_background_plate_is_reported()
    {
        Scene scene = CreateScene();
        AddRoundedRect(scene, "Title backing plate", zIndex: 8, width: 560, height: 150, x: 160, y: 0);
        AddText(scene, "Launch notes", zIndex: 10, x: -120, y: 0);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
            issue.Category == "textBackgroundFit"
            && issue.Severity == "major"
            && issue.ElementIds.Count >= 2));
    }

    [Test]
    public async Task Saturated_dark_teal_cyan_magenta_palette_is_reported()
    {
        Scene scene = CreateScene();
        AddRect(scene, "Deep teal background", zIndex: 0, width: 1920, height: 1080, color: Color.Parse("#ff020711"));
        AddRoundedRect(scene, "Cyan panel", zIndex: 5, width: 420, height: 120, x: -260, color: Color.Parse("#ff00ffff"));
        AddText(scene, "Launch notes", zIndex: 10, fill: Color.Parse("#ffff00ff"));

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "paletteHarmony" && issue.Severity == "major"));
            Assert.That(result.Metrics.Palette.HasDarkTealCyanMagentaPalette, Is.True);
        });
    }

    [Test]
    public async Task Material_ui_card_texture_is_reported()
    {
        Scene scene = CreateScene();
        AddCard(scene, "Card A", zIndex: 4, x: -260);
        AddCard(scene, "Card B", zIndex: 5, x: 260);
        AddText(scene, "Launch notes", zIndex: 10);

        QualityReviewResponse result = await AnalyzeAsync(scene, evaluateMotion: false);

        Assert.That(result.Issues, Has.Some.Matches<QualityIssue>(issue =>
            issue.Category == "materialUiLook" && issue.Severity == "major"));
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

    private static ValueTask<QualityReviewResponse> AnalyzeAsync(Scene scene, bool evaluateMotion = true)
        => new QualityAnalyzer(new MotionVariationAnalyzer(new StillRenderer())).AnalyzeAsync(
            scene,
            timeSeconds: null,
            sampleCount: 3,
            renderScale: 1,
            styleProfile: null,
            allowAllCaps: false,
            allowHardCuts: false,
            allowRectDominance: false,
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
        Color? fill = null)
    {
        var block = new TextBlock
        {
            Name = text,
            Text = { CurrentValue = text },
            Size = { CurrentValue = 72 },
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
