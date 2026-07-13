using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Regression gate for the SwiftShader teardown crash: disposing a Vulkan-backed
/// <see cref="RenderTarget"/> while the shared <c>GRContext</c> still holds recorded-but-unflushed
/// ops targeting (or sampling) its image must not leave those pending command buffers pointing at
/// a destroyed VkImage. Without the drain in <c>VulkanTexture2D.Dispose</c> the next global flush
/// executes them against freed memory — a native SIGSEGV/SIGBUS inside libvk_swiftshader (the CPU
/// driver faults; MoltenVK/macOS never runs the wrapped-VkImage path, so this cannot crash on macOS).
/// </summary>
[NonParallelizable]
[TestFixture]
public class VulkanTexturePendingOpsDisposeTests
{
    [OneTimeSetUp]
    public void SetUp()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void DisposingRenderTarget_WithItsOwnOpsStillPending_NextGlobalFlushIsSafe()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var target = RenderTarget.Create(256, 256)!;
            var canvas = new ImmediateCanvas(target);
            canvas.Clear(Colors.Red);
            canvas.DrawRectangle(new Rect(8, 8, 200, 200), Brushes.Resource.White, null);

            // Destroys the backing VkImage while the draws above are still recorded-but-unflushed
            // (an intermediate that is rendered but never sampled is exactly this shape).
            target.Dispose();

            // Heap churn: freed driver allocations must actually be reused before the use-after-free
            // becomes observable on SwiftShader (an untouched freed block reads back benignly).
            IGraphicsContext context = GraphicsContextFactory.SharedContext!;
            for (int i = 0; i < 64; i++)
            {
                context.CreateTexture2D(256, 256, TextureFormat.RGBA16Float).Dispose();
            }

            // Executes any orphaned ops explicitly; ImmediateCanvas.Dispose intentionally performs no implicit
            // per-draw synchronization after FR-008 centralization.
            GraphicsContextFactory.SharedContext!.SkiaContext.Flush(submit: true, synchronous: true);
            canvas.Dispose();
        });
        Assert.Pass();
    }
}
