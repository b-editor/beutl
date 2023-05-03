using System.Diagnostics.CodeAnalysis;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.Graphics.Transformation;

public abstract class Transform : Animatable, IMutableTransform
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static Transform()
    {
        IsEnabledProperty = ConfigureProperty<bool, Transform>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Transform>(IsEnabledProperty);
    }

    protected Transform()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public static ITransform Identity { get; } = new IdentityTransform();

    public abstract Matrix Value { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public static bool TryParse(string s, [NotNullWhen(true)] out ITransform? transform)
    {
        return TryParse(s.AsSpan(), out transform);
    }

    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out ITransform? transform)
    {
        try
        {
            transform = Parse(s);
            return true;
        }
        catch
        {
            transform = null;
            return false;
        }
    }

    public static ITransform Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    public static ITransform Parse(ReadOnlySpan<char> s)
    {
        return TransformParser.Parse(s);
    }

    public ITransform ToImmutable()
    {
        return new ImmutableTransform(Value, _isEnabled);
    }

    protected static void AffectsRender<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Transform
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                if (e.OldValue is IAffectsRender oldAffectsRender)
                    oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                if (e.NewValue is IAffectsRender newAffectsRender)
                    newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
            }
        }

        property1?.Changed.Subscribe(onNext);
        property2?.Changed.Subscribe(onNext);
        property3?.Changed.Subscribe(onNext);
        property4?.Changed.Subscribe(onNext);
    }

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Transform
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                    if (e.NewValue is IAffectsRender newAffectsRender)
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                }
            });
        }
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    private sealed class IdentityTransform : ITransform
    {
        public Matrix Value => Matrix.Identity;

        public bool IsEnabled => true;
    }
}
