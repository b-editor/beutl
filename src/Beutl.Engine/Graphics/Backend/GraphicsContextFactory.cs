using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Backend;

internal class GraphicsContextFactory
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(GraphicsContextFactory));
    private static bool s_failedToInitialize;

    public static IGraphicsContext? SharedContext { get; private set; }

    public static IGraphicsContext CreateContext()
    {
        if (OperatingSystem.IsMacOS())
            return new CompositeContext();

        return new VulkanContext();
    }

    public static IGraphicsContext? GetOrCreateShared()
    {
        if (s_failedToInitialize)
            return null;

        if (SharedContext == null)
        {
            RenderThread.Dispatcher.VerifyAccess();

            try
            {
                SharedContext = CreateContext();
            }
            catch (Exception e)
            {
                s_logger.LogError(e, "Failed to initialize shared graphics context.");
                s_failedToInitialize = true;
            }
        }

        return SharedContext;
    }

    public static void Shutdown()
    {
        RenderThread.Dispatcher.Invoke(() => { SharedContext?.Dispose(); });
    }
}
