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

    [TestCase(CpuSafeDrawable.Shape)]
    [TestCase(CpuSafeDrawable.Text)]
    [TestCase(CpuSafeDrawable.UnscaledBitmap)]
    [TestCase(CpuSafeDrawable.SkslRuntimeShader)]
    [TestCase(CpuSafeDrawable.Particle)]
    public async Task Cpu_safe_content_renders_png_without_gpu_requirement(CpuSafeDrawable drawable)
    {
        string dir = TestWorkspace.CreateDirectory();
        string output = Path.Combine(dir, "still.png");
        Scene scene = CreateScene(dir, CreateDrawable(drawable, dir));

        var renderer = new StillRenderer();
        RenderStillResponse result = await renderer.RenderAsync(scene, MediaTime.Zero, output, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(output), Is.True);
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(0));
            Assert.That(result.Width, Is.EqualTo(160));
            Assert.That(result.Height, Is.EqualTo(90));
            Assert.That(result.VisibilityAnalysis, Is.Not.Null);
            Assert.That(result.VisibilityAnalysis!.TotalPixels, Is.EqualTo(160 * 90));
            Assert.That(result.ActiveElements, Has.Count.EqualTo(1));
            Assert.That(result.ActiveElements![0].ObjectCount, Is.GreaterThanOrEqualTo(1));
            if (drawable is CpuSafeDrawable.Shape or CpuSafeDrawable.Text or CpuSafeDrawable.SkslRuntimeShader)
            {
                Assert.That(result.Warnings, Is.Empty);
            }
        });
    }

    [Test]
    public async Task Empty_scene_render_still_returns_no_warnings_and_low_visibility()
    {
        string dir = TestWorkspace.CreateDirectory();
        string output = Path.Combine(dir, "empty.png");
        var scene = new Scene(160, 90, "empty")
        {
            Duration = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };

        var renderer = new StillRenderer();
        RenderStillResponse result = await renderer.RenderAsync(scene, MediaTime.Zero, output, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(output), Is.True);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.VisibilityAnalysis!.VisiblePixelRatio, Is.LessThan(0.005));
            Assert.That(result.ActiveElements, Is.Empty);
        });
    }

    [Test]
    public async Task Render_still_measures_foreground_confined_to_one_quadrant_without_aesthetic_warnings()
    {
        string dir = TestWorkspace.CreateDirectory();
        string output = Path.Combine(dir, "confined.png");
        Scene scene = CreateConfinedChangingScene(dir);

        var renderer = new StillRenderer();
        RenderStillResponse result = await renderer.RenderAsync(scene, MediaTime.FromSeconds(0.5), output, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(output), Is.True);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.VisibilityAnalysis!.ForegroundPixelRatio, Is.GreaterThan(0));
            Assert.That(result.VisibilityAnalysis.OccupiedBoundsRatio, Is.LessThan(0.12));
            Assert.That(result.VisibilityAnalysis.MaxQuadrantForegroundRatio, Is.GreaterThanOrEqualTo(0.90));
            Assert.That(result.ActiveElements, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Gpu_required_3d_scene_renders_when_available_or_reports_rendering_unavailable()
    {
        string dir = TestWorkspace.CreateDirectory();
        string output = Path.Combine(dir, "still-3d.png");
        Scene scene = CreateScene(dir, new Scene3D());

        var renderer = new StillRenderer();
        if (TestGpu.IsAvailable)
        {
            RenderStillResponse result = await renderer.RenderAsync(scene, MediaTime.Zero, output, 1, CancellationToken.None);

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
            await Assert.ThrowsAsync<RenderingUnavailableException>(async () =>
                await renderer.RenderAsync(scene, MediaTime.Zero, output, 1, CancellationToken.None));
        }
    }

    [Test]
    public async Task Frame_measurement_reports_static_and_changing_pixels()
    {
        string staticDir = TestWorkspace.CreateDirectory();
        Scene staticScene = CreateScene(staticDir, new RectShape());
        staticScene.Duration.Value = MediaTime.FromSeconds(3);

        string changingDir = TestWorkspace.CreateDirectory();
        Scene changingScene = CreateChangingScene(changingDir);

        var analyzer = new FrameDifferenceAnalyzer(new StillRenderer());
        FrameDifferenceResponse staticResult = await analyzer.AnalyzeAsync(
            staticScene,
            [MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(1.5), MediaTime.FromSeconds(2.5)],
            1,
            48,
            24,
            CancellationToken.None);
        FrameDifferenceResponse changingResult = await analyzer.AnalyzeAsync(
            changingScene,
            [MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(1.5), MediaTime.FromSeconds(2.5)],
            1,
            48,
            24,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(staticResult.MinimumChangedPixelRatio, Is.EqualTo(0));
            Assert.That(changingResult.MinimumChangedPixelRatio, Is.GreaterThan(0.01));
        });
    }

    [Test]
    public async Task Frame_measurement_reports_one_quadrant_coverage()
    {
        string dir = TestWorkspace.CreateDirectory();
        Scene confinedScene = CreateConfinedChangingScene(dir);

        var analyzer = new FrameDifferenceAnalyzer(new StillRenderer());
        FrameDifferenceResponse result = await analyzer.AnalyzeAsync(
            confinedScene,
            [MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(1.5), MediaTime.FromSeconds(2.5)],
            1,
            48,
            24,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.FrameCoverage, Has.All.Matches<MotionFrameCoverage>(item =>
                item.OccupiedBoundsRatio <= 0.35 && item.MaxQuadrantForegroundRatio >= 0.90));
        });
    }

    // Read as raw half-float bytes, the 16 and 20 fills pass both thresholds; read as sRGB they pass neither.
    [Test]
    public async Task Frame_measurement_compares_srgb_brightness()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(160, 90, "dark-steps")
        {
            Duration = { Value = MediaTime.FromSeconds(3) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        AddColorRectElement(scene, dir, "16.belm", MediaTime.Zero, MediaTime.FromSeconds(1), 160, 90, Color.FromRgb(16, 16, 16));
        AddColorRectElement(scene, dir, "20.belm", MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), 160, 90, Color.FromRgb(20, 20, 20));
        AddColorRectElement(scene, dir, "64.belm", MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), 160, 90, Color.FromRgb(64, 64, 64));

        var analyzer = new FrameDifferenceAnalyzer(new StillRenderer());
        FrameDifferenceResponse result = await analyzer.AnalyzeAsync(
            scene,
            [MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(1.5), MediaTime.FromSeconds(2.5)],
            1,
            48,
            24,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.FrameCoverage.Select(item => item.ForegroundPixels).Take(2), Is.All.Zero);
            Assert.That(result.FrameCoverage[2].ForegroundPixelRatio, Is.GreaterThan(0.99));
            Assert.That(result.PairVariations[0].ChangedPixels, Is.Zero);
            Assert.That(result.PairVariations[1].ChangedPixelRatio, Is.GreaterThan(0.99));
            Assert.That(result.PairVariations[0].MeanAbsoluteDelta, Is.EqualTo(4 / 255d).Within(1 / 255d));
            Assert.That(result.PairVariations[1].MeanAbsoluteDelta, Is.EqualTo(44 / 255d).Within(1 / 255d));
        });
    }

    [Test]
    public async Task Empty_scene_kinetic_ribbon_starter_renders_visible_text()
    {
        string dir = TestWorkspace.CreateDirectory();
        string withTextPath = Path.Combine(dir, "with-text.png");
        string withoutTextPath = Path.Combine(dir, "without-text.png");
        var scene = new Scene(640, 360, "example")
        {
            Duration = { Value = MediaTime.FromSeconds(8) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        using var session = new AgentToolkitTestSession(scene);

        DeclarativeExample example = new SchemaGenerator()
            .Generate(typeFilter: nameof(TextBlock))
            .Examples
            .Single(item => item.Name == "create-empty-scene-kinetic-ribbon-title");
        JsonObject current = session.Documents.Read(scene);
        JsonObject desired = (JsonObject)MergePatchApplier.Apply(current, example.Patch)!;

        new Reconciler().Apply(session, desired);

        TextBlock title = scene.Children
            .SelectMany(element => element.Objects)
            .OfType<TextBlock>()
            .Single(item => item.Text.Value == "Beutl motion");

        var renderer = new StillRenderer();
        await renderer.RenderAsync(scene, MediaTime.FromSeconds(2), withTextPath, 1, CancellationToken.None);

        title.Text.Value = string.Empty;
        await renderer.RenderAsync(scene, MediaTime.FromSeconds(2), withoutTextPath, 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Has.Count.EqualTo(example.Patch["Elements"]!.AsArray().Count));
            Assert.That(CountDifferingPixels(withTextPath, withoutTextPath), Is.GreaterThan(500));
        });
    }

    [Test]
    public async Task Rendered_text_bound_is_attributed_to_its_owning_element()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(160, 90, "text-owner")
        {
            Duration = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var text = new TextBlock
        {
            Name = { Value = "headline" },
            Text = { Value = "HELLO" },
            Size = { Value = 48 },
            Fill = { Value = new SolidColorBrush(Colors.White) }
        };
        var element = new Element
        {
            Name = { Value = "owner" },
            Length = { Value = MediaTime.FromSeconds(1) },
            ZIndex = { Value = 7 },
            Uri = new Uri(Path.Combine(dir, "owner.belm"))
        };
        element.AddObject(text);
        scene.Children.Add(element);

        var renderer = new StillRenderer();
        using RenderedFrameAnalysis frame = await renderer.RenderFrameAnalysisAsync(
            scene, MediaTime.Zero, 1f, CancellationToken.None);

        RenderedTextBounds bound = frame.TextBounds.Single();
        Assert.Multiple(() =>
        {
            Assert.That(bound.TextBlock.Name.Value, Is.EqualTo("headline"));
            // The bound is attributed via textBlock.FindHierarchicalParent<Element>(), which must
            // resolve to the owning element rather than falling back or naming a different one.
            Assert.That(bound.Element, Is.SameAs(element));
        });
    }

    [Test]
    public void Contains_gpu_only_content_ignores_a_disabled_scene3d_object()
    {
        string dir = TestWorkspace.CreateDirectory();
        Scene enabled = CreateScene(dir, new Scene3D());
        Scene disabled = CreateScene(dir, new Scene3D { IsEnabled = { Value = false } });

        Assert.Multiple(() =>
        {
            Assert.That(StillRenderer.ContainsGpuOnlyContent(enabled), Is.True);
            // The renderer skips a disabled Scene3D, so it must not force the GPU-only requirement.
            Assert.That(StillRenderer.ContainsGpuOnlyContent(disabled), Is.False);
        });
    }

    private static Scene CreateScene(string dir, EngineObject drawable)
    {
        var scene = new Scene(160, 90, "still")
        {
            Duration = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(1) },
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
            Duration = { Value = MediaTime.FromSeconds(3) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        AddColorRectElement(scene, dir, "red.belm", MediaTime.Zero, MediaTime.FromSeconds(1), 160, 90, Colors.Red);
        AddColorRectElement(scene, dir, "blue.belm", MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), 160, 90, Colors.Blue);
        AddColorRectElement(scene, dir, "green.belm", MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), 160, 90, Colors.Lime);
        return scene;
    }

    private static Scene CreateConfinedChangingScene(string dir)
    {
        var scene = new Scene(160, 90, "confined-motion")
        {
            Duration = { Value = MediaTime.FromSeconds(3) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };

        AddColorRectElement(scene, dir, "red.belm", MediaTime.Zero, MediaTime.FromSeconds(1), 40, 30, Colors.Red, -50, -30);
        AddColorRectElement(scene, dir, "blue.belm", MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), 40, 30, Colors.Blue, -50, -30);
        AddColorRectElement(scene, dir, "green.belm", MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), 40, 30, Colors.Lime, -50, -30);
        return scene;
    }

    private static void AddColorRectElement(
        Scene scene,
        string dir,
        string fileName,
        MediaTime start,
        MediaTime length,
        float width,
        float height,
        Color color,
        float translateX = 0,
        float translateY = 0)
    {
        var element = new Element
        {
            Start = { Value = start },
            Length = { Value = length },
            Uri = new Uri(Path.Combine(dir, fileName))
        };
        var rect = new RectShape
        {
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = new SolidColorBrush(color) }
        };

        if (translateX != 0 || translateY != 0)
        {
            rect.Transform.Value = new TransformGroup
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
            CpuSafeDrawable.Text => new TextBlock { Text = { Value = "Agent" } },
            CpuSafeDrawable.UnscaledBitmap => CreateSourceImage(dir),
            CpuSafeDrawable.SkslRuntimeShader => new RectShape { FilterEffect = { Value = new SKSLScriptEffect() } },
            CpuSafeDrawable.Particle => new ParticleEmitter
            {
                MaxParticles = { Value = 4 },
                EmissionRate = { Value = 4 },
                ParticleDrawable = { Value = new EllipseShape() }
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
        return new SourceImage { Source = { Value = source } };
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
