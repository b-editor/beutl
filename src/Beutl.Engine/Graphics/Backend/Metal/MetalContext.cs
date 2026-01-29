using System.Runtime.InteropServices;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Metal;

internal sealed class MetalContext : IDisposable
{
    private readonly IntPtr _metalDevice;
    private readonly IntPtr _commandQueue;
    private readonly GRContext _grContext;
    private bool _disposed;

    private static readonly IntPtr s_newCommandQueueSelector = GetValidSelector("newCommandQueue");
    private static readonly IntPtr s_commandBufferSelector = GetValidSelector("commandBuffer");
    private static readonly IntPtr s_commitSelector = GetValidSelector("commit");
    private static readonly IntPtr s_waitUntilCompletedSelector = GetValidSelector("waitUntilCompleted");
    private static readonly IntPtr s_releaseSelector = GetValidSelector("release");

    public MetalContext()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("Metal is only supported on macOS/iOS");
        }

        // Get default Metal device
        _metalDevice = MTLCreateSystemDefaultDevice();
        if (_metalDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create Metal device");
        }

        try
        {
            // Create command queue
            _commandQueue = objc_msgSend_IntPtr(_metalDevice, s_newCommandQueueSelector);
            if (_commandQueue == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal command queue");
            }

            try
            {
                // Create SkiaSharp Metal context
                var backendContext = new GRMtlBackendContext
                {
                    DeviceHandle = _metalDevice,
                    QueueHandle = _commandQueue
                };

                _grContext = GRContext.CreateMetal(backendContext);
                if (_grContext == null)
                {
                    throw new InvalidOperationException("Failed to create SkiaSharp Metal context");
                }
            }
            catch
            {
                ReleaseObject(_commandQueue);
                throw;
            }
        }
        catch
        {
            ReleaseObject(_metalDevice);
            throw;
        }
    }

    public GraphicsBackend Backend => GraphicsBackend.Metal;

    public GRContext SkiaContext => _grContext;

    public void WaitIdle()
    {
        // Create a command buffer and commit it with waitUntilCompleted
        var commandBuffer = objc_msgSend_IntPtr(_commandQueue, s_commandBufferSelector);
        if (commandBuffer != IntPtr.Zero)
        {
            objc_msgSend_void(commandBuffer, s_commitSelector);
            objc_msgSend_void(commandBuffer, s_waitUntilCompletedSelector);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _grContext?.Dispose();

        // Release Metal objects
        ReleaseObject(_commandQueue);
        ReleaseObject(_metalDevice);
    }

    private static IntPtr GetValidSelector(string selectorName)
    {
        var selector = sel_getUid(selectorName);
        if (selector == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to get Objective-C selector: {selectorName}");
        }
        return selector;
    }

    private static void ReleaseObject(IntPtr obj)
    {
        if (obj != IntPtr.Zero && s_releaseSelector != IntPtr.Zero)
        {
            objc_msgSend_void(obj, s_releaseSelector);
        }
    }

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_getUid")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);
}
