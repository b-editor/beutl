using System.Runtime.InteropServices;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal sealed class MetalContext
{
    private readonly IntPtr _metalDevice;
    private readonly IntPtr _commandQueue;
    private readonly GRContext _grContext;
    private bool _disposed;

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

        // Create command queue
        _commandQueue = objc_msgSend_IntPtr(_metalDevice, sel_getUid("newCommandQueue"));
        if (_commandQueue == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create Metal command queue");
        }

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

    public GraphicsBackend Backend => GraphicsBackend.Metal;

    public GRContext SkiaContext => _grContext;

    public void WaitIdle()
    {
        // Create a command buffer and commit it with waitUntilCompleted
        var commandBuffer = objc_msgSend_IntPtr(_commandQueue, sel_getUid("commandBuffer"));
        if (commandBuffer != IntPtr.Zero)
        {
            objc_msgSend_void(commandBuffer, sel_getUid("commit"));
            objc_msgSend_void(commandBuffer, sel_getUid("waitUntilCompleted"));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _grContext?.Dispose();

        // Release Metal objects
        if (_commandQueue != IntPtr.Zero)
        {
            objc_msgSend_void(_commandQueue, sel_getUid("release"));
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
