using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>A render target could not be allocated at the requested device size.</summary>
public sealed class RenderTargetAllocationException : InvalidOperationException
{
    /// <summary>Initializes a failure with the requested size and, when known, the applicable limit.</summary>
    public RenderTargetAllocationException(PixelSize deviceSize, int? maxDimension = null, Exception? innerException = null)
        : base(maxDimension is { } limit
            ? $"A {deviceSize.Width}x{deviceSize.Height} pixel render target exceeds the {limit} pixels this device can attach."
            : $"The render-target factory could not allocate {deviceSize.Width}x{deviceSize.Height} pixels.", innerException)
    {
        DeviceSize = deviceSize;
        MaxDimension = maxDimension;
        IsBudgetRefusal = maxDimension.HasValue;
    }

    internal RenderTargetAllocationException(PixelSize deviceSize, long liveBytes, long maximumLiveBytes)
        : base(string.Format(MessageStrings.RenderTargetLiveByteBudgetExceeded,
            deviceSize.Width, deviceSize.Height, maximumLiveBytes, liveBytes))
    {
        DeviceSize = deviceSize;
        IsBudgetRefusal = true;
    }

    /// <summary>Gets the requested allocation size in device pixels.</summary>
    public PixelSize DeviceSize { get; }

    /// <summary>Gets the per-axis limit when it caused the refusal.</summary>
    public int? MaxDimension { get; }

    internal bool IsBudgetRefusal { get; }
}
