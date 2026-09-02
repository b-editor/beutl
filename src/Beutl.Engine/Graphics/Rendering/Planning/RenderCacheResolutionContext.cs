namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderCacheResolutionContext
{
    public RenderCacheResolutionContext(
        RenderCacheFormatIdentity format,
        RenderCacheDeviceContextIdentity deviceContext,
        bool allowPersistentLookup = true,
        bool allowCapturePublication = true,
        Vector deviceGridOffset = default)
    {
        format.ThrowIfUninitialized(nameof(format));
        deviceContext.ThrowIfUninitialized(nameof(deviceContext));
        Format = format;
        DeviceContext = deviceContext;
        AllowPersistentLookup = allowPersistentLookup;
        AllowCapturePublication = allowCapturePublication;
        DeviceGridOffset = deviceGridOffset;
    }

    public RenderCacheFormatIdentity Format { get; }

    public RenderCacheDeviceContextIdentity DeviceContext { get; }

    public bool AllowPersistentLookup { get; }

    public bool AllowCapturePublication { get; }

    public Vector DeviceGridOffset { get; }
}
