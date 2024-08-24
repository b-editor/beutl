using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

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

    public static unsafe void CopyFromCPU(MemoryBuffer1D<Vec4b, Stride1D.Dense> source, SKSurface surface, SKImageInfo imageInfo)
    {
        void* tmp = NativeMemory.Alloc((nuint)source.LengthInBytes);
        try
        {
            bool result = surface.ReadPixels(imageInfo, (nint)tmp, imageInfo.Width * 4, 0, 0);

            source.View.CopyFromCPU(ref Unsafe.AsRef<Vec4b>(tmp), source.Length);
        }
        finally
        {
            NativeMemory.Free(tmp);
        }
    }

    public static unsafe void CopyToCPU(MemoryBuffer1D<Vec4b, Stride1D.Dense> source, SKBitmap bitmap)
    {
        source.View.CopyToCPU(ref Unsafe.AsRef<Vec4b>((void*)bitmap.GetPixels()), source.Length);
    }

    public static unsafe void CopyFromCPU(MemoryBuffer2D<Vec4b, Stride2D.DenseX> source, SKSurface surface, SKImageInfo imageInfo)
    {
        void* tmp = NativeMemory.Alloc((nuint)source.LengthInBytes);
        try
        {
            bool result = surface.ReadPixels(imageInfo, (nint)tmp, imageInfo.Width * 4, 0, 0);

            source.View.BaseView.CopyFromCPU(ref Unsafe.AsRef<Vec4b>(tmp), source.Length);
        }
        finally
        {
            NativeMemory.Free(tmp);
        }
    }

    public static unsafe void CopyToCPU(MemoryBuffer2D<Vec4b, Stride2D.DenseX> source, SKBitmap bitmap)
    {
        source.View.BaseView.CopyToCPU(ref Unsafe.AsRef<Vec4b>((void*)bitmap.GetPixels()), source.Length);
    }
}
