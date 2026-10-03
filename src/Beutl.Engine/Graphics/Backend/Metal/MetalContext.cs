using System.Runtime.InteropServices;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Metal;

internal sealed class MetalContext : IDisposable
{
    private readonly IntPtr _metalDevice;
    private readonly IntPtr _commandQueue;
    private readonly GRContext _grContext;
    // Retained hand-off command buffers that may still be running; see WaitForHandOffs.
    private readonly List<IntPtr> _handOffCommandBuffers = [];
    private bool _disposed;

    private static readonly IntPtr s_newCommandQueueSelector = GetValidSelector("newCommandQueue");
    private static readonly IntPtr s_commandBufferSelector = GetValidSelector("commandBuffer");
    private static readonly IntPtr s_commitSelector = GetValidSelector("commit");
    private static readonly IntPtr s_waitUntilCompletedSelector = GetValidSelector("waitUntilCompleted");
    private static readonly IntPtr s_releaseSelector = GetValidSelector("release");
    private static readonly IntPtr s_encodeWaitForEventSelector = GetValidSelector("encodeWaitForEvent:value:");
    private static readonly IntPtr s_retainSelector = GetValidSelector("retain");
    private static readonly IntPtr s_statusSelector = GetValidSelector("status");

    // MTLCommandBufferStatusCompleted and MTLCommandBufferStatusError.
    private const nuint CompletedStatus = 4;
    private const nuint ErrorStatus = 5;

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

    /// <summary>Holds every command buffer Skia commits after this call until <paramref name="sharedEvent"/> reaches <paramref name="value"/>.</summary>
    /// <remarks>A wait blocks its queue, not only its own command buffer, so later Skia submissions stay behind it.</remarks>
    public void CommitWaitForEvent(IntPtr sharedEvent, ulong value)
    {
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr commandBuffer = CreateCommandBuffer();
            objc_msgSend_void(commandBuffer, s_encodeWaitForEventSelector, sharedEvent, value);
            CommitHandOff(commandBuffer);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Waits until every hand-off command buffer committed so far has finished.</summary>
    /// <remarks>
    /// A queue finishes its command buffers out of order, so an empty command buffer committed now can complete
    /// before an earlier event wait; only the hand-off buffers themselves say when the shared event is no longer
    /// in use. Signals the waits depend on have to be submitted already.
    /// </remarks>
    public void WaitForHandOffs()
    {
        foreach (IntPtr commandBuffer in _handOffCommandBuffers)
        {
            objc_msgSend_void(commandBuffer, s_waitUntilCompletedSelector);
            ReleaseObject(commandBuffer);
        }

        _handOffCommandBuffers.Clear();
    }

    private void CommitHandOff(IntPtr commandBuffer)
    {
        // Retire the hand-offs that already finished, so the list stays as short as the work in flight.
        _handOffCommandBuffers.RemoveAll(static buffer =>
        {
            nuint status = (nuint)objc_msgSend_IntPtr(buffer, s_statusSelector);
            if (status != CompletedStatus && status != ErrorStatus)
                return false;

            ReleaseObject(buffer);
            return true;
        });

        objc_msgSend_void(commandBuffer, s_commitSelector);
        // The command buffer is autoreleased; keep it beyond the caller's pool until it is known to be finished.
        objc_msgSend_IntPtr(commandBuffer, s_retainSelector);
        _handOffCommandBuffers.Add(commandBuffer);
    }

    private IntPtr CreateCommandBuffer()
    {
        IntPtr commandBuffer = objc_msgSend_IntPtr(_commandQueue, s_commandBufferSelector);
        return commandBuffer != IntPtr.Zero
            ? commandBuffer
            : throw new InvalidOperationException("Failed to create a Metal command buffer.");
    }

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

        WaitForHandOffs();
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

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr sharedEvent, ulong value);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
