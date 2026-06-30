using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Custom-shader scale-uniform tests: SKSL iScale, GLSL Width/Height (device px), and GLSL `scale` push constant.
[NonParallelizable]
[TestFixture]
public class ShaderScaleUniformTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static Drawable.Resource Make(Func<FilterEffect> makeEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;
        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    // ---- (a) scale-unaware SKSL: a pure UV expression, independent of device density. ----
    private const string SkslUvScript =
        """
        uniform shader src;
        uniform float2 iResolution;

        half4 main(float2 fragCoord) {
            half4 c = src.eval(fragCoord);
            float2 uv = fragCoord / iResolution;
            if (uv.y < 0.5) {
                return half4(1.0, 0.0, 0.0, 1.0);
            }
            return c;
        }
        """;

    private static FilterEffect MakeSkslUvEffect()
    {
        var e = new SKSLScriptEffect();
        e.Script.CurrentValue = SkslUvScript;
        return e;
    }

    [Test]
    public void Sksl_ScaleUnaware_ScaleOne_IsDeterministicByteIdentical()
    {
        Assert.That(new SKSLScriptEffect().ValidateScript(SkslUvScript).Error, Is.Null, "UV SKSL must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(Make(MakeSkslUvEffect), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(Make(MakeSkslUvEffect), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    // Vacuity guard: the UV shader must actually change the render.
    [Test]
    public void Sksl_ScaleUnaware_ChangesRender()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap shaded = GoldenImageHarness.RenderAtScale(Make(MakeSkslUvEffect), Frame, 1f);
            using Bitmap plain = GoldenImageHarness.RenderAtScale(Make(() => new FilterEffectGroup()), Frame, 1f);
            double ssim = ImageMetrics.Ssim(shaded, plain);
            TestContext.WriteLine($"[Sksl UV guard] shaded vs plain SSIM={ssim:F4}");
            Assert.That(ssim, Is.LessThan(0.99),
                "UV SKSL did not change the render — likely failed to compile, making the byte-identity test vacuous");
        });
    }

    // ---- (b) scale-aware SKSL reading iScale at reduced scale (s_out=0.5). ----
    private const string SkslDiscScript =
        """
        uniform shader src;
        uniform float2 iResolution;
        uniform float iScale;

        half4 main(float2 fragCoord) {
            half4 c = src.eval(fragCoord);
            float2 center = iResolution * 0.5;
            float radius = 28.0 * iScale;
            if (length(fragCoord - center) <= radius) {
                return half4(0.0, 1.0, 0.0, 1.0);
            }
            return c;
        }
        """;

    private static FilterEffect MakeSkslDiscEffect()
    {
        var e = new SKSLScriptEffect();
        e.Script.CurrentValue = SkslDiscScript;
        return e;
    }

    [Test]
    public void Sksl_iScaleAware_ReducedScale_MatchesReference()
    {
        Assert.That(new SKSLScriptEffect().ValidateScript(SkslDiscScript).Error, Is.Null, "iScale disc SKSL must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(MakeSkslDiscEffect), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(Make(MakeSkslDiscEffect), Frame, 0.5f);
            // Upscales the 0.5 render and asserts SSIM >= 0.985 / MAE <= 0.02 against the 1.0 reference: the
            // iScale-shrunk disc must occupy the same logical region.
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }

    // ---- (c) GLSL anchored to device-px Width/Height. Border of fixed logical thickness (10 px). ----
    private const string GlslDevicePxBorder =
        """
        #version 450
        layout(location = 0) in vec2 fragCoord;     // normalized 0..1
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PushConstants {
            float progress;
            float duration;
            float time;
            float width;    // device px (= ceil(logicalBounds.W * w))
            float height;   // device px
        } pc;

        void main() {
            vec4 c = texture(srcTexture, fragCoord);
            vec2 devCoord = fragCoord * vec2(pc.width, pc.height);
            float w = pc.width / 200.0;         // working scale from device size vs the 200-logical-px frame
            float border = 10.0 * w;            // 10 LOGICAL px -> device px
            if (devCoord.x < border || devCoord.y < border ||
                devCoord.x >= pc.width - border || devCoord.y >= pc.height - border) {
                outColor = vec4(1.0, 0.0, 0.0, 1.0);
            } else {
                outColor = c;
            }
        }
        """;

    private static FilterEffect MakeGlslBorderEffect()
    {
        var e = new GLSLScriptEffect();
        e.FragmentShader.CurrentValue = GlslDevicePxBorder;
        return e;
    }

    // Vacuity guard: the GLSL shader must compile AND visibly change the render, so the parity test below is
    // not passing on a silent no-op.
    [Test]
    public void Glsl_DevicePx_CompilesAndApplies()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.That(new GLSLScriptEffect().ValidateScript(GlslDevicePxBorder).Error, Is.Null, "GLSL border must compile");

            using Bitmap bordered = GoldenImageHarness.RenderAtScale(Make(MakeGlslBorderEffect), Frame, 1f);
            using Bitmap plain = GoldenImageHarness.RenderAtScale(Make(() => new FilterEffectGroup()), Frame, 1f);
            double ssim = ImageMetrics.Ssim(bordered, plain);
            TestContext.WriteLine($"[GLSL guard] bordered vs plain SSIM={ssim:F4}");
            Assert.That(ssim, Is.LessThan(0.99),
                "GLSL border did not change the render — the shader likely failed to compile/apply");
        });
    }

    [Test]
    public void Glsl_DevicePx_Supersampled_KeepsLogicalAppearance()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(Make(MakeGlslBorderEffect), Frame, 1f);
            using Bitmap hi = GoldenImageHarness.RenderAtScale(Make(MakeGlslBorderEffect), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

            string? nonFinite = ImageMetrics.FirstNonFinite(("1:1", r1), ("2x", hi), ("2x-delivered", delivered));
            Assert.That(nonFinite, Is.Null, $"non-finite GLSL render: {nonFinite}");

            double ssim = ImageMetrics.Ssim(r1, delivered);
            TestContext.WriteLine($"[GLSL device-px border] 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                "supersampled GLSL border diverged from 1:1 — Width/Height did not carry device px (× w)");
        });
    }

    // ---- (d)/(e) scale-aware GLSL reading the `scale` push constant (GLSL analogue of SKSL iScale disc). ----
    private const string GlslScaleDisc =
        """
        #version 450
        layout(location = 0) in vec2 fragCoord;     // normalized 0..1
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PushConstants {
            float progress;
            float duration;
            float time;
            float width;    // device px
            float height;   // device px
            float scale;    // working scale w
        } pc;

        void main() {
            vec4 c = texture(srcTexture, fragCoord);
            vec2 devCoord = fragCoord * vec2(pc.width, pc.height);
            vec2 center = vec2(pc.width, pc.height) * 0.5;
            float radius = 28.0 * pc.scale;          // 28 LOGICAL px -> device px via the scale uniform
            if (length(devCoord - center) <= radius) {
                outColor = vec4(0.0, 1.0, 0.0, 1.0);
            } else {
                outColor = c;
            }
        }
        """;

    private static FilterEffect MakeGlslScaleDiscEffect()
    {
        var e = new GLSLScriptEffect();
        e.FragmentShader.CurrentValue = GlslScaleDisc;
        return e;
    }

    // Vacuity guard: the scale-aware GLSL shader must compile AND visibly change the render, so the parity
    // test below is not passing on a silent no-op.
    [Test]
    public void Glsl_ScaleAware_CompilesAndApplies()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.That(new GLSLScriptEffect().ValidateScript(GlslScaleDisc).Error, Is.Null, "scale-aware GLSL must compile");

            using Bitmap disc = GoldenImageHarness.RenderAtScale(Make(MakeGlslScaleDiscEffect), Frame, 1f);
            using Bitmap plain = GoldenImageHarness.RenderAtScale(Make(() => new FilterEffectGroup()), Frame, 1f);
            double ssim = ImageMetrics.Ssim(disc, plain);
            TestContext.WriteLine($"[GLSL scale guard] disc vs plain SSIM={ssim:F4}");
            Assert.That(ssim, Is.LessThan(0.99),
                "scale-aware GLSL disc did not change the render — the shader likely failed to compile/apply");
        });
    }

    [Test]
    public void Glsl_ScaleAware_ReducedScale_MatchesReference()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(MakeGlslScaleDiscEffect), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(Make(MakeGlslScaleDiscEffect), Frame, 0.5f);
            // The `scale`-shrunk disc must occupy the same logical region: upscale the 0.5 render and assert it
            // matches the 1.0 reference within SSIM/MAE.
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }
}
