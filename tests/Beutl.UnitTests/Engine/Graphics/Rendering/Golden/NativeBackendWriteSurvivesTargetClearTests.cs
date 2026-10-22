using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
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
            effect.FragmentShader.Value = InvertShader;

            var rectangle = new RectShape();
            rectangle.Width.Value = 120;
            rectangle.Height.Value = 80;
            rectangle.Fill.Value = new SolidColorBrush(Colors.OrangeRed);
            rectangle.FilterEffect.Value = effect;

            using Bitmap bitmap = SceneRenderHarness.RenderSingle(
                rectangle,
                new PixelSize(256, 144),
                outputScale,
                outputScale * 2f,
                MediaTime.Zero);

            Assert.That(
                ImageMetrics.CountCoveredPixels(bitmap),
                Is.GreaterThan(0),
                "the GLSL effect wrote the target through the Vulkan backend, so its output must survive "
                + "the transparent clear the allocator issues.");
        });
    }
}
