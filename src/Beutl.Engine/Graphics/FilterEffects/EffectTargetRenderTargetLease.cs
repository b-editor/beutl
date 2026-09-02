using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

internal sealed class EffectTargetRenderTargetLease : IDisposable
{
    private SharedLease? _sharedLease;
    private RenderTarget? _target;
    private readonly bool _ownsTargetReference;

    public EffectTargetRenderTargetLease(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        _sharedLease = new SharedLease(lease);
        _target = lease.Target;
    }

    public RenderTarget Target
    {
        get
        {
            ObjectDisposedException.ThrowIf(_sharedLease is null, this);
            return _target!;
        }
    }

    public EffectTargetRenderTargetLease Retain()
    {
        SharedLease sharedLease = _sharedLease
            ?? throw new ObjectDisposedException(nameof(EffectTargetRenderTargetLease));
        sharedLease.Retain();
        try
        {
            return new EffectTargetRenderTargetLease(
                sharedLease,
                Target.ShallowCopy());
        }
        catch
        {
            sharedLease.Release();
            throw;
        }
    }

    public RenderTarget TransferToAcceptedCache()
    {
        SharedLease sharedLease = _sharedLease
            ?? throw new ObjectDisposedException(nameof(EffectTargetRenderTargetLease));
        return sharedLease.TransferToAcceptedCache();
    }

    public void Dispose()
    {
        SharedLease? sharedLease = _sharedLease;
        if (sharedLease is null)
            return;

        _sharedLease = null;
        RenderTarget? target = _target;
        _target = null;
        try
        {
            if (_ownsTargetReference)
                target?.Dispose();
        }
        finally
        {
            sharedLease.Release();
        }
    }

    private EffectTargetRenderTargetLease(SharedLease sharedLease, RenderTarget target)
    {
        _sharedLease = sharedLease;
        _target = target;
        _ownsTargetReference = true;
    }

    private sealed class SharedLease(RenderTargetLease lease)
    {
        private RenderTargetLease? _lease = lease;
        private int _references = 1;

        public void Retain()
        {
            ObjectDisposedException.ThrowIf(_references == 0, this);
            _references = checked(_references + 1);
        }

        public void Release()
        {
            if (_references == 0)
                return;

            _references--;
            if (_references == 0)
            {
                _lease?.ReleaseForBackendReuse();
                _lease = null;
            }
        }

        public RenderTarget TransferToAcceptedCache()
        {
            ObjectDisposedException.ThrowIf(_references == 0, this);
            RenderTargetLease activeLease = _lease
                ?? throw new InvalidOperationException(
                    "The effect-target lease has already transferred into a persistent cache.");
            RenderTarget target = activeLease.TransferToAcceptedCache();
            _lease = null;
            return target;
        }
    }
}
