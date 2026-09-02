namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderCacheDeviceContextIdentity(
    object DeviceIdentity,
    object ContextIdentity)
{
    public void ThrowIfUninitialized(string parameterName)
    {
        if (DeviceIdentity is null || ContextIdentity is null)
        {
            throw new ArgumentException(
                "A render-cache device identity requires non-null device and context components.",
                parameterName);
        }
    }
}
