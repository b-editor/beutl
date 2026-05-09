using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="GLSLShader"/> は <c>GraphicsContextFactory.SharedContext</c> がないと一切動作しない。
/// Vulkan 経由で実コンパイル/実行できるかをテストする。
/// </summary>
[NonParallelizable]
public class GLSLShaderTests
{
    private const string ConstantBlueFragment = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() {
            outColor = vec4(0.0, 0.0, 1.0, 1.0);
        }
        """;

    private const string MalformedFragment = """
        #version 450
        layout(location = 0) out vec4 outColor;
        void main() {
            outColor = NOT_A_VALID_GLSL_TOKEN;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct DummyPush { public float Dummy; }

    [Test]
    public void TryCreate_ValidShader_Succeeds()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            bool ok = GLSLShader.TryCreate(ConstantBlueFragment, out var shader, out var error);

            try
            {
                Assert.That(ok, Is.True, $"Compile failed: {error}");
                Assert.That(shader, Is.Not.Null);
                Assert.That(error, Is.Null);
            }
            finally
            {
                shader?.Dispose();
            }
        });
    }

    [Test]
    public void TryCreate_EmptySource_ReturnsFailureSynchronously()
    {
        VulkanTestEnvironment.EnsureAvailable();

        bool ok = GLSLShader.TryCreate("   ", out var shader, out var error);

        Assert.That(ok, Is.False);
        Assert.That(shader, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TryCreate_InvalidSource_ReturnsFailureWithErrorText()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            bool ok = GLSLShader.TryCreate(MalformedFragment, out var shader, out var error);

            Assert.That(ok, Is.False);
            Assert.That(shader, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Create_InvalidSource_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.Throws<InvalidOperationException>(() => GLSLShader.Create(MalformedFragment));
        });
    }

    [Test]
    public void Apply_AfterDispose_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.Dispose();

            using var targets = new EffectTargets();
            var ctx = CreateCustomContext(targets);

            Assert.Throws<ObjectDisposedException>(() =>
                shader.Apply<DummyPush>(ctx, new DummyPush()));
        });
    }

    [Test]
    public void Apply_OverwritesTargetWithShaderOutput()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var targets = new EffectTargets();

            // Set up a 4x4 red EffectTarget so we can detect the shader's blue overwrite.
            using var sourceRenderTarget = RenderTarget.Create(4, 4);
            Assume.That(sourceRenderTarget, Is.Not.Null);
            using (var canvas = new ImmediateCanvas(sourceRenderTarget!))
            {
                canvas.Clear(Colors.Red);
            }

            targets.Add(new EffectTarget(sourceRenderTarget!, new Rect(0, 0, 4, 4)));

            var customCtx = CreateCustomContext(targets);

            using var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.Apply<DummyPush>(customCtx, new DummyPush());

            // After Apply, the EffectTarget at index 0 should be replaced with the shader output.
            var resultTarget = targets[0];
            Assert.That(resultTarget.RenderTarget, Is.Not.Null);
            Assert.That(resultTarget.RenderTarget!.Texture, Is.Not.Null);
            Assert.That(resultTarget.RenderTarget.Width, Is.EqualTo(4));

            ctx.WaitIdle();

            // Sample the resulting texture pixels.
            byte[] pixels = resultTarget.RenderTarget.Texture!.DownloadPixels();
            // RGBA16Float: 8 bytes per pixel
            Assert.That(pixels.Length, Is.EqualTo(4 * 4 * 8));

            // First pixel should be (0, 0, 1, 1).
            float r = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 0));
            float g = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 2));
            float b = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 4));
            Assert.That(r, Is.EqualTo(0f).Within(0.01f));
            Assert.That(g, Is.EqualTo(0f).Within(0.01f));
            Assert.That(b, Is.EqualTo(1f).Within(0.01f));
        });
    }

    private static CustomFilterEffectContext CreateCustomContext(EffectTargets targets)
        => new CustomFilterEffectContext(targets);
}
