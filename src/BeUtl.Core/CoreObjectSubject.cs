using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace BeUtl;

internal sealed class CoreObjectSubject<T> : SubjectBase<T>
{
    private readonly CoreProperty<T> _property;
    private readonly List<IObserver<T>> _list = new();
    private bool _isDisposed;
    private ICoreObject? _object;

    public CoreObjectSubject(ICoreObject o, CoreProperty<T> property)
    {
        _object = o;
        _property = property;
        o.PropertyChanged += Object_PropertyChanged;
    }

    public override bool HasObservers => _list.Count > 0;

    [MemberNotNullWhen(false, "_object")]
    public override bool IsDisposed => _isDisposed;

    public override void Dispose()
    {
        if (!IsDisposed)
        {
            _object.PropertyChanged -= Object_PropertyChanged;
            _list.Clear();
            _object = null;
            _isDisposed = true;
        }
    }

    public override void OnCompleted()
    {
    }

    public override void OnError(Exception error)
    {
    }

    public override void OnNext(T value)
    {
        _object?.SetValue(_property, value);
    }

    public override IDisposable Subscribe(IObserver<T> observer)
    {
        if (observer is null) throw new ArgumentNullException(nameof(observer));
        if (IsDisposed) throw new ObjectDisposedException(nameof(CoreObjectSubject<T>));

        _list.Add(observer);

        try
        {
            observer.OnNext(_object.GetValue(_property));
        }
        catch (Exception ex)
        {
            observer.OnError(ex);
        }

        return Disposable.Create((observer, _list), o =>
        {
            o.observer.OnCompleted();
            o._list.Remove(o.observer);
        });
    }

    private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _property.Name) return;
        if (IsDisposed) throw new ObjectDisposedException(nameof(CoreObjectSubject<T>));

        T value = _object.GetValue(_property);
        foreach (IObserver<T>? item in _list)
        {
            try
            {
                item.OnNext(value);
            }
            catch (Exception ex)
            {
                item.OnError(ex);
            }
        }
    }
}
