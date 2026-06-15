using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003 / FR-014 / contracts/shader-uniforms.md — the custom-shader scale-uniform contract:
//   SKSL adds iScale = w; width/height/iResolution/fragCoord report DEVICE px (ceil(bounds × w)).
//   GLSL adds a `scale` push constant = w (the clamped buffer density), mirroring SKSL's iScale; its
//   Width/Height also carry device px.
// EffectScaleParityTests already covers the SKSL iScale parity (its "SKSLScript-Border" case). This fixture
// fills the remaining gaps the contract names: (a) a scale-UNAWARE SKSL shader is byte-identical at w == 1
// (the backward-compat guarantee), (b)/(c) a GLSL shader anchored to device-px Width/Height keeps its logical
// appearance across scales, and (d)/(e) a GLSL shader reading the new `scale` push constant keeps a fixed
// LOGICAL size across scales — the direct GLSL analogue of the SKSL iScale disc test.
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

    // ---- (a) scale-UNAWARE SKSL: a pure UV expression (no absolute-px literal, no iScale). It is independent
    // of the device density, so at w == 1 the shader path must be deterministic / unchanged from today —
    // captured as render-twice byte-equality (the in-suite analogue, since the suite pins no golden PNG). ----
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
        Assert.That(SKSLScriptEffect.ValidateScript(SkslUvScript), Is.Null, "UV SKSL must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(Make(MakeSkslUvEffect), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(Make(MakeSkslUvEffect), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }

    // Vacuity guard: the UV shader must actually change the render (else byte-identity is trivially true for a
    // no-op that failed to compile/apply). Compare against an effect-free render.
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

    // ---- (b) scale-AWARE SKSL reading iScale at a REDUCED scale. EffectScaleParityTests already covers the
    // SUPERSAMPLE (s_out=2) direction; this covers the reduced-preview (s_out=0.5) direction, where iScale < 1
    // must shrink an absolute-px literal so the disc keeps the SAME logical radius. If iScale did NOT plumb
    // through (stayed 1.0), the disc would be twice the logical size at 0.5 and the reduced-vs-1.0 comparison
    // would fail. A fixed-logical-radius (28 px) green disc, centred via iResolution. ----
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
        Assert.That(SKSLScriptEffect.ValidateScript(SkslDiscScript), Is.Null, "iScale disc SKSL must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(MakeSkslDiscEffect), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(Make(MakeSkslDiscEffect), Frame, 0.5f);
            // Upscales the 0.5 render and asserts SSIM >= 0.985 / MAE <= 0.02 against the 1.0 reference: the
            // iScale-shrunk disc must occupy the same logical region at reduced scale.
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }

    // ---- (c) GLSL anchored to DEVICE-px Width/Height. fragCoord is normalized 0..1 (the fullscreen
    // triangle in GLSLFilterPipeline), so device px = fragCoord * size. A border of FIXED LOGICAL thickness
    // (10 px) requires deriving the working scale from the device-px Width (w = pc.width / 200). If Width did
    // NOT carry device px (× w), the border would thin/shift at w == 2 and the supersampled-then-downscaled
    // render would diverge from the 1:1 render. ----
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
    // meaningful rather than passing on a silent no-op.
    [Test]
    public void Glsl_DevicePx_CompilesAndApplies()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.That(GLSLScriptEffect.ValidateScript(GlslDevicePxBorder), Is.Null, "GLSL border must compile");

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

    // ---- (d)/(e) scale-AWARE GLSL reading the new `scale` push constant — the direct analogue of the SKSL
    // iScale disc. The shader does NOT recover w from Width/Height (which it cannot do without knowing the
    // logical bounds); it reads pc.scale directly and multiplies an absolute-px radius by it, so a fixed
    // LOGICAL-radius (28 px) green disc keeps the same logical size at a reduced scale. If `scale` did NOT
    // plumb through (stayed 1.0), the disc would be twice the logical size at w == 0.5 and the
    // reduced-vs-1.0 comparison would fail. fragCoord is normalized 0..1 (fullscreen triangle), so device
    // px = fragCoord * (Width, Height). ----
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

    // Vacuity guard: the scale-aware GLSL shader must compile AND visibly change the render so the parity
    // test below is meaningful rather than passing on a silent no-op.
    [Test]
    public void Glsl_ScaleAware_CompilesAndApplies()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.That(GLSLScriptEffect.ValidateScript(GlslScaleDisc), Is.Null, "scale-aware GLSL must compile");

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
            // The `scale`-shrunk disc must occupy the same logical region at reduced scale: upscale the 0.5
            // render and assert it matches the 1.0 reference within SSIM/MAE.
            GoldenImageHarness.AssertReducedScaleExact(full, half);
        });
    }
}
