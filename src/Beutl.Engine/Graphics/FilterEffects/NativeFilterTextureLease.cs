using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

internal sealed class NativeFilterTextureLease : IDisposable
{
    private ITexture2D? _texture;
    private RenderTargetLease? _renderTargetLease;
    private readonly bool _ownsTexture;

    private NativeFilterTextureLease(
        ITexture2D texture,
        RenderTargetLease? renderTargetLease,
        bool ownsTexture)
    {
        _texture = texture;
        _renderTargetLease = renderTargetLease;
        _ownsTexture = ownsTexture;
    }

    public ITexture2D Texture
        => _texture ?? throw new ObjectDisposedException(nameof(NativeFilterTextureLease));

    public static NativeFilterTextureLease Own(ITexture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new NativeFilterTextureLease(texture, renderTargetLease: null, ownsTexture: true);
    }

    public static NativeFilterTextureLease Lease(ITexture2D texture, RenderTargetLease renderTargetLease)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(renderTargetLease);
        return new NativeFilterTextureLease(texture, renderTargetLease, ownsTexture: false);
    }

    public void Dispose()
    {
        ITexture2D? texture = _texture;
        if (texture is null)
            return;

        _texture = null;
        if (_ownsTexture)
            texture.Dispose();
        else
            _renderTargetLease?.Dispose();
        _renderTargetLease = null;
    }
}
