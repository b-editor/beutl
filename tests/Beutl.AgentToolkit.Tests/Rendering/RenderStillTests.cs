using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using SkiaSharp;
using MergePatchApplier = Beutl.AgentToolkit.MergePatch.MergePatch;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderStillTests
{
    [Test]
    public void Bare_render_output_paths_default_to_agent_output_directory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderTools.NormalizeOutputPath("preview.png"), Is.EqualTo(Path.Combine("agent-output", "preview.png")));
            Assert.That(RenderTools.NormalizeOutputPath(Path.Combine("nested", "preview.png")), Is.EqualTo(Path.Combine("nested", "preview.png")));
            Assert.That(RenderTools.NormalizeOutputPath(Path.GetFullPath("preview.png")), Is.EqualTo(Path.GetFullPath("preview.png")));
        });
    }

    [TestCase(CpuSafeDrawable.Shape)]
    [TestCase(CpuSafeDrawable.Text)]
    [TestCase(CpuSafeDrawable.UnscaledBitmap)]
    [TestCase(CpuSafeDrawable.SkslRuntimeShader)]
    [TestCase(CpuSafeDrawable.Particle)]
    public async Task Cpu_safe_content_renders_png_without_gpu_requirement(CpuSafeDrawable drawable)
    {
        string dir = CreateWorkspace();
        string output = Path.Combine(dir, "still.png");
        Scene scene = CreateScene(dir, CreateDrawable(drawable, dir));

        var renderer = new StillRenderer();
        RenderStillResponse result = await renderer.RenderAsync(scene, TimeSpan.Zero, output, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(output), Is.True);
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(0));
            Assert.That(result.Width, Is.EqualTo(160));
            Assert.That(result.Height, Is.EqualTo(90));
        });
    }

    [Test]
    public async Task Gpu_required_3d_scene_renders_when_available_or_reports_rendering_unavailable()
    {
        string dir = CreateWorkspace();
        string output = Path.Combine(dir, "still-3d.png");
        Scene scene = CreateScene(dir, new Scene3D());

        var renderer = new StillRenderer();
        if (AgentToolkitGpuTestEnvironment.IsAvailable)
        {
            RenderStillResponse result = await renderer.RenderAsync(scene, TimeSpan.Zero, output, 1, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(output), Is.True);
                Assert.That(new FileInfo(output).Length, Is.GreaterThan(0));
                Assert.That(result.Width, Is.EqualTo(160));
                Assert.That(result.Height, Is.EqualTo(90));
            });
        }
        else
        {
            Assert.ThrowsAsync<RenderingUnavailableException>(async () =>
                await renderer.RenderAsync(scene, TimeSpan.Zero, output, 1, CancellationToken.None));
        }
    }

    [Test]
    public async Task Motion_variation_analysis_flags_static_scenes_and_accepts_temporal_changes()
    {
        string staticDir = CreateWorkspace();
        Scene staticScene = CreateScene(staticDir, new RectShape());
        staticScene.Duration = TimeSpan.FromSeconds(3);

        string changingDir = CreateWorkspace();
        Scene changingScene = CreateChangingScene(changingDir);

        var analyzer = new MotionVariationAnalyzer(new StillRenderer());
        MotionVariationResponse staticResult = await analyzer.AnalyzeAsync(
            staticScene,
            [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5)],
            1,
            0.01,
            48,
            0.35,
            0.90,
            24,
            CancellationToken.None);
        MotionVariationResponse changingResult = await analyzer.AnalyzeAsync(
            changingScene,
            [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5)],
            1,
            0.01,
            48,
            0.35,
            0.90,
            24,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(staticResult.PassesMinimumMotion, Is.False);
            Assert.That(staticResult.Verdict, Is.EqualTo("low-motion-variation"));
            Assert.That(staticResult.MinimumChangedPixelRatio, Is.EqualTo(0));
            Assert.That(changingResult.PassesMinimumMotion, Is.True);
            Assert.That(changingResult.Verdict, Is.EqualTo("motion-variation-ok"));
            Assert.That(changingResult.MinimumChangedPixelRatio, Is.GreaterThan(0.01));
        });
    }

    [Test]
    public async Task Motion_variation_analysis_flags_sustained_one_quadrant_frame_coverage()
    {
        string dir = CreateWorkspace();
        Scene confinedScene = CreateConfinedChangingScene(dir);

        var analyzer = new MotionVariationAnalyzer(new StillRenderer());
        MotionVariationResponse result = await analyzer.AnalyzeAsync(
            confinedScene,
            [TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5)],
            1,
            0.01,
            48,
            0.35,
            0.90,
            24,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.PassesTemporalMotion, Is.True);
            Assert.That(result.PassesFrameCoverage, Is.False);
            Assert.That(result.PassesMinimumMotion, Is.False);
            Assert.That(result.Verdict, Is.EqualTo("poor-frame-coverage"));
            Assert.That(result.FrameCoverage, Has.All.Matches<MotionFrameCoverage>(item =>
                item.OccupiedBoundsRatio <= 0.35 && item.MaxQuadrantForegroundRatio >= 0.90));
            Assert.That(result.ReviewNotes, Has.Some.Contains("confined"));
        });
    }

    [Test]
    public async Task Empty_scene_motion_graphics_example_renders_visible_text()
    {
        string dir = CreateWorkspace();
        string withTextPath = Path.Combine(dir, "with-text.png");
        string withoutTextPath = Path.Combine(dir, "without-text.png");
        var scene = new Scene(640, 360, "example")
        {
            Duration = TimeSpan.FromSeconds(8),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        using var session = new AgentToolkitTestSession(scene);

        DeclarativeExample example = new SchemaGenerator()
            .Generate(typeFilter: nameof(TextBlock))
            .Examples
            .Single(item => item.Name == "create-empty-scene-motion-graphics");
        JsonObject current = session.Documents.Read(scene);
        JsonObject desired = (JsonObject)MergePatchApplier.Apply(current, example.Patch)!;

        new Reconciler().Apply(session, desired);

        TextBlock title = scene.Children
            .SelectMany(element => element.Objects)
            .OfType<TextBlock>()
            .Single(item => item.Text.CurrentValue == "BEUTL MOTION");

        var renderer = new StillRenderer();
        await renderer.RenderAsync(scene, TimeSpan.FromSeconds(2), withTextPath, 1, CancellationToken.None);

        title.Text.CurrentValue = string.Empty;
        await renderer.RenderAsync(scene, TimeSpan.FromSeconds(2), withoutTextPath, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Has.Count.EqualTo(3));
            Assert.That(CountDifferingPixels(withTextPath, withoutTextPath), Is.GreaterThan(500));
        });
    }

    private static Scene CreateScene(string dir, EngineObject drawable)
    {
        var scene = new Scene(160, 90, "still")
        {
            Duration = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(drawable);
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateChangingScene(string dir)
    {
        var scene = new Scene(160, 90, "motion-check")
        {
            Duration = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        AddColorRectElement(scene, dir, "red.belm", TimeSpan.Zero, TimeSpan.FromSeconds(1), 160, 90, Colors.Red);
        AddColorRectElement(scene, dir, "blue.belm", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 160, 90, Colors.Blue);
        AddColorRectElement(scene, dir, "green.belm", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 160, 90, Colors.Lime);
        return scene;
    }

    private static Scene CreateConfinedChangingScene(string dir)
    {
        var scene = new Scene(160, 90, "confined-motion")
        {
            Duration = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };

        AddColorRectElement(scene, dir, "red.belm", TimeSpan.Zero, TimeSpan.FromSeconds(1), 40, 30, Colors.Red, -50, -30);
        AddColorRectElement(scene, dir, "blue.belm", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 40, 30, Colors.Blue, -50, -30);
        AddColorRectElement(scene, dir, "green.belm", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 40, 30, Colors.Lime, -50, -30);
        return scene;
    }

    private static void AddColorRectElement(
        Scene scene,
        string dir,
        string fileName,
        TimeSpan start,
        TimeSpan length,
        float width,
        float height,
        Color color,
        float translateX = 0,
        float translateY = 0)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            Uri = new Uri(Path.Combine(dir, fileName))
        };
        var rect = new RectShape
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color) }
        };

        if (translateX != 0 || translateY != 0)
        {
            rect.Transform.CurrentValue = new TransformGroup
            {
                Children = { new TranslateTransform(translateX, translateY) }
            };
        }

        element.AddObject(rect);
        scene.Children.Add(element);
    }

    private static EngineObject CreateDrawable(CpuSafeDrawable drawable, string dir)
    {
        return drawable switch
        {
            CpuSafeDrawable.Shape => new RectShape(),
            CpuSafeDrawable.Text => new TextBlock { Text = { CurrentValue = "Agent" } },
            CpuSafeDrawable.UnscaledBitmap => CreateSourceImage(dir),
            CpuSafeDrawable.SkslRuntimeShader => new RectShape { FilterEffect = { CurrentValue = new SKSLScriptEffect() } },
            CpuSafeDrawable.Particle => new ParticleEmitter
            {
                MaxParticles = { CurrentValue = 4 },
                EmissionRate = { CurrentValue = 4 },
                ParticleDrawable = { CurrentValue = new EllipseShape() }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(drawable), drawable, null)
        };
    }

    private static SourceImage CreateSourceImage(string dir)
    {
        string path = Path.Combine(dir, "source.png");
        using (var bitmap = new Bitmap(8, 8))
        {
            Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        }

        var source = new ImageSource();
        source.ReadFrom(new Uri(path));
        return new SourceImage { Source = { CurrentValue = source } };
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int CountDifferingPixels(string firstPath, string secondPath)
    {
        using SKBitmap? first = SKBitmap.Decode(firstPath);
        using SKBitmap? second = SKBitmap.Decode(secondPath);
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first!.Width, Is.EqualTo(second!.Width));
        Assert.That(first.Height, Is.EqualTo(second.Height));

        int count = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                SKColor a = first.GetPixel(x, y);
                SKColor b = second.GetPixel(x, y);
                int delta = Math.Abs(a.Red - b.Red)
                            + Math.Abs(a.Green - b.Green)
                            + Math.Abs(a.Blue - b.Blue)
                            + Math.Abs(a.Alpha - b.Alpha);
                if (delta > 48)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public enum CpuSafeDrawable
    {
        Shape,
        Text,
        UnscaledBitmap,
        SkslRuntimeShader,
        Particle
    }
}
