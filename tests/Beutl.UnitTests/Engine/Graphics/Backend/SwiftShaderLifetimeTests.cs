using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
[Category("GpuPassFusionGpu")]
public sealed class SwiftShaderLifetimeTests
{
    [TestCase("enumerate")]
    [TestCase("render")]
    [TestCase("restart")]
    public Task Shutdown_ExitsNormallyWithTheBundledDriver(string action)
    {
        string manifest = GetBundledDriverManifest();
        return TestWorkerProgram.RunAsync(
            start => start.Environment["VK_DRIVER_FILES"] = manifest,
            TestWorkerProgram.SwiftShaderLifetimeWorkerArgument,
            action);
    }

    private static string GetBundledDriverManifest()
    {
        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string manifest = Path.Combine(
            AppContext.BaseDirectory, "runtimes", $"linux-{architecture}", "native", "vk_swiftshader_icd.json");
        Assert.That(File.Exists(manifest), Is.True, "The regression must exercise the bundled SwiftShader driver.");
        return manifest;
    }

    internal static void RunWorker(string action)
    {
        BeutlHomeIsolation.Begin("beutl-swiftshader-lifetime");
        try
        {
            string manifest = GetBundledDriverManifest();
            Assert.That(Environment.GetEnvironmentVariable("VK_DRIVER_FILES"), Is.EqualTo(manifest),
                "The worker must start with the bundled ICD selected before native Vulkan discovery.");

            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                int cycles = action == "restart" ? 3 : 1;
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    GraphicsDeviceInfo driver = GraphicsContextFactory.GetAvailableDevices()
                        .Single(device => device.Name.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase));
                    Assert.That(GraphicsContextFactory.SelectGpuByName(driver.Name), Is.True);

                    if (action != "enumerate")
                    {
                        Assert.That(GraphicsContextFactory.GetOrCreateShared(), Is.Not.Null);
                        using RenderTarget target = RenderTarget.Create(8, 8)
                            ?? throw new InvalidOperationException("Could not allocate the probe render target.");
                        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
                        canvas.Clear(Colors.Red);
                        using Bitmap snapshot = target.Snapshot();
                        Assert.That(snapshot.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
                    }

                    GraphicsContextFactory.Shutdown();
                    Assert.Multiple(() =>
                    {
                        Assert.That(GraphicsContextFactory.SharedContext, Is.Null);
                        Assert.That(GraphicsContextFactory.VulkanInstance, Is.Null);
                    });
                }
            });
        }
        finally
        {
            BeutlHomeIsolation.End();
        }
    }
}
