using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ILGPU;
using ILGPU.Runtime;

using Microsoft.Extensions.Logging;

namespace Beutl.Rendering;

public class SharedGPUContext
{
    private static readonly ILogger<SharedGPUContext> s_logger = BeutlApplication.Current.LoggerFactory.CreateLogger<SharedGPUContext>();

    private static Context? s_context;
    private static Device? s_device;
    private static Accelerator? s_accelerator;

    public static Context Context => s_context!;

    public static Device Device => s_device!;

    public static Accelerator Accelerator => s_accelerator!;

    public static void Create()
    {
        RenderThread.Dispatcher.VerifyAccess();

        s_context = Context.Create(builder => builder.Default().EnableAlgorithms());

        s_device = s_context.GetPreferredDevice(false);

        using var sw = new StringWriter();
        s_device.PrintInformation(sw);
        s_logger.LogInformation("ILGPU.Runtime.Device.PrintInformation: {Info}", sw.ToString());

        s_accelerator = s_device.CreateAccelerator(s_context);
    }

    public static void Shutdown()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            s_accelerator?.Dispose();
            s_accelerator = null;
            s_device = null;
            s_context?.Dispose();
            s_context = null;
        });
    }
}
