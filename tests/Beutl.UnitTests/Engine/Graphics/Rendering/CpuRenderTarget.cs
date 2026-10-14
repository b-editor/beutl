using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>A CPU target over the linear-sRGB, premultiplied RgbaF16 surface that renderers request.</summary>
internal sealed class CpuRenderTarget(PixelSize size)
    : RenderTarget(
        SKSurface.Create(new SKImageInfo(
                size.Width,
                size.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("Could not create a CPU test surface."),
        size.Width,
        size.Height)
{
    public CpuRenderTarget(int width, int height)
        : this(new PixelSize(width, height))
    {
    }
}

/// <summary>Allocates every target a renderer requests as a <see cref="CpuRenderTarget"/>, counting them.</summary>
internal sealed class CpuRenderTargetFactory : IRenderTargetFactory
{
    public int AllocationCount { get; private set; }

    public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
    {
        AllocationCount++;
        return new CpuRenderTarget(allocation.DeviceSize);
    }
}

/// <summary>
/// Records every CPU target it allocates, and can decline one allocation, throw on creation, or make a target
/// throw when it is disposed.
/// </summary>
internal sealed class FailureTestTargetFactory(
    int? failAt = null,
    Exception? createFailure = null,
    Func<int, Exception?>? disposeFailure = null) : IRenderTargetFactory, IDisposable
{
    public List<FailureTestRenderTarget> Targets { get; } = [];

    public int CreateCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
    {
        PixelSize deviceSize = allocation.DeviceSize;
        int index = CreateCalls++;
        if (createFailure is not null)
            throw createFailure;
        if (index == failAt)
            return null;

        var target = new FailureTestRenderTarget(deviceSize, disposeFailure?.Invoke(index));
        Targets.Add(target);
        return target;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal sealed class FailureTestRenderTarget : RenderTarget
{
    private readonly Exception? _disposeFailure;

    public FailureTestRenderTarget(PixelSize size, Exception? disposeFailure = null)
        : base(
            SKSurface.Create(new SKImageInfo(
                size.Width,
                size.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            size.Width,
            size.Height)
    {
        _disposeFailure = disposeFailure;
    }

    public int DisposeCalls { get; private set; }

    protected override void Dispose(bool disposing)
    {
        bool shouldThrow = disposing && !IsDisposed && _disposeFailure is not null;
        if (disposing && !IsDisposed)
            DisposeCalls++;
        base.Dispose(disposing);
        if (shouldThrow)
            throw _disposeFailure!;
    }
}
