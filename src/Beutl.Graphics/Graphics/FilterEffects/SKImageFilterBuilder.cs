using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class SKImageFilterBuilder : IDisposable
{
    private SKImageFilter? _filter;

    public void AppendSkiaFilter<T>(T data, Func<T, SKImageFilter?, SKImageFilter?> factory)
    {
        SKImageFilter? input = _filter;
        _filter = factory(data, input);
        input?.Dispose();
    }

    public void AppendSkiaFilter<T>(T data, FilterEffectActivator activator, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> factory)
    {
        SKImageFilter? input = _filter;
        _filter = factory(data, input, activator);
        input?.Dispose();
    }

    public bool HasFilter() => _filter != null;

    public SKImageFilter? GetFilter()
    {
        return _filter;
    }

    public void Clear()
    {
        _filter?.Dispose();
        _filter = null;
    }

    public void Dispose()
    {
        Clear();
    }
}

