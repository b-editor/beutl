using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class SKImageFilterBuilder : IDisposable
{
    private SKImageFilter? _filter;
    private SKColorFilter? _colorFilter;

    public void AppendSkiaFilter<T>(T data, FilterEffectActivator activator, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory)
    {
        SKImageFilter? inner = GetFilter();
        SKImageFilter? outer = factory(data, inner, activator);
        if (outer != null)
        {
            _filter = outer;
            inner?.Dispose();
        }
    }

    public void AppendSKColorFilter<T>(T data, FilterEffectActivator activator, Func<T, FilterEffectActivator, SKColorFilter?> factory)
    {
        SKColorFilter? inner = _colorFilter;
        SKColorFilter? outer = factory(data, activator);

        if (outer != null && inner != null)
        {
            _colorFilter = SKColorFilter.CreateCompose(outer, inner);
            inner.Dispose();
            outer.Dispose();
        }
        else if (outer != null)
        {
            _colorFilter = outer;
        }
    }

    public bool HasFilter() => _filter != null || _colorFilter != null;

    public SKImageFilter? GetFilter()
    {
        if (_colorFilter != null)
        {
            SKImageFilter? inner = _filter;
            _filter = SKImageFilter.CreateColorFilter(_colorFilter, inner);
            inner?.Dispose();
        }

        return _filter;
    }

    public void Clear()
    {
        _filter?.Dispose();
        _filter = null;
        _colorFilter?.Dispose();
        _colorFilter = null;
    }

    public void Dispose()
    {
        Clear();
    }
}

