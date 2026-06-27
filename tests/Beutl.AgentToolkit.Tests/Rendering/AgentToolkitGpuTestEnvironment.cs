using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.AgentToolkit.Tests.Rendering;

internal static class AgentToolkitGpuTestEnvironment
{
    public static bool IsAvailable => RenderThread.Dispatcher.Invoke(() => GraphicsContextFactory.GetOrCreateShared() is not null);

    public static void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            Assert.Ignore("No Vulkan/MoltenVK graphics context is available on this host.");
        }
    }
}
