using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class NativeBackendWriteSurvivesTargetClearTests
{
    private const string InvertShader = """
        #version 450
        layout(location = 0) in vec2 vTexCoord;
        layout(location = 0) out vec4 fragColor;
        layout(binding = 0) uniform sampler2D uTexture;
        void main()
        {
            vec4 src = texture(uTexture, vTexCoord);
            fragColor = vec4(src.a - src.r, src.a - src.g, src.a - src.b, src.a);
        }
        """;

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void GlslEffectOutput_IsNotBlankedByTheTargetClear(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new GLSLScriptEffect();
            ScriptCompilationResult compilation = effect.ValidateScript(InvertShader);
            Assert.That(
                compilation.Status,
                Is.EqualTo(ScriptCompilationStatus.Compiled),
                $"the fixture's shader must compile, otherwise the effect degrades to a no-op: {compilation.Error}");
            effect.FragmentShader.CurrentValue = InvertShader;

            var rectangle = new RectShape();
            rectangle.Width.CurrentValue = 120;
            rectangle.Height.CurrentValue = 80;
            rectangle.Fill.CurrentValue = new SolidColorBrush(Colors.OrangeRed);
            rectangle.FilterEffect.CurrentValue = effect;

            var scene = new Scene(256, 144, "glsl-clear")
            {
                Uri = new Uri("file:///glsl-clear/scene"),
            };
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(4),
                ZIndex = 0,
                IsEnabled = true,
                Uri = new Uri("file:///glsl-clear/element"),
            };
            element.AddObject(rectangle);
            scene.Children.Add(element);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview, outputScale, false, outputScale * 2f)
            {
                CacheOptions = RenderCacheOptions.Disabled,
            };
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.Zero));
            using Bitmap bitmap = renderer.Snapshot();

            long opaque = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y)[..(bitmap.Width * 4)];
                for (int x = 3; x < row.Length; x += 4)
                {
                    if ((float)BitConverter.UInt16BitsToHalf(row[x]) > 0.5f)
                        opaque++;
                }
            }

            Assert.That(
                opaque,
                Is.GreaterThan(0),
                "the GLSL effect wrote the target through the Vulkan backend, so its output must survive "
                + "the transparent clear the allocator issues.");
        });
    }
}
