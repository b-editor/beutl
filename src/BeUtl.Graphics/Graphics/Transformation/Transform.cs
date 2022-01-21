using BeUtl.Styling;

namespace BeUtl.Graphics.Transformation;

public abstract class Transform : Styleable, ITransform
{
    public event EventHandler? Invalidated;

    public abstract Matrix Value { get; }

    protected static void AffectRender<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Transform
    {
        Action<ElementPropertyChangedEventArgs> onNext = e =>
        {
            if (e.Sender is T s)
            {
                s.RaiseInvalidated();
            }
        };

        property1?.Changed.Subscribe(onNext);
        property2?.Changed.Subscribe(onNext);
        property3?.Changed.Subscribe(onNext);
        property4?.Changed.Subscribe(onNext);
    }

    protected static void AffectRender<T>(params CoreProperty[] properties)
        where T : Transform
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated();
                }
            });
        }
    }

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
