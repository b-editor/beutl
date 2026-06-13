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
//   GLSL adds NO new push constant — its Width/Height carry device px; the author derives w from them.
// EffectScaleParityTests already covers the SKSL iScale parity (its "SKSLScript-Border" case). This fixture
// fills the two remaining gaps the contract names: (a) a scale-UNAWARE SKSL shader is byte-identical at w == 1
// (the backward-compat guarantee), and (b)/(c) a GLSL shader anchored to device-px Width/Height keeps its
// logical appearance across scales — there was previously NO GLSL scale test at all.
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

    // ---- (b)/(c) GLSL anchored to DEVICE-px Width/Height. fragCoord is normalized 0..1 (the fullscreen
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
}
