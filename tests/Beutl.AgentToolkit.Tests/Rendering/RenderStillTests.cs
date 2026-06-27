using Beutl.AgentToolkit.Rendering;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

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

    public enum CpuSafeDrawable
    {
        Shape,
        Text,
        UnscaledBitmap,
        SkslRuntimeShader,
        Particle
    }
}
