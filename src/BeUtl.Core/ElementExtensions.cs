using System.ComponentModel;
using System.Reactive.Subjects;

using BeUtl.Reactive;

namespace BeUtl;

public static class ElementExtensions
{
    public static ICoreObject? Find(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (ICoreObject item in self.LogicalChildren)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static ICoreObject? FindAllChildren(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (ICoreObject item in self.EnumerateAllChildren<ICoreObject>())
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static IObservable<CorePropertyChangedEventArgs<T>> GetPropertyChangedObservable<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CorePropertyChangedObservable<T>(obj, property);
    }

    public static IObservable<T> GetObservable<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CoreObjectSubject<T>(obj, property);
    }

    private sealed class CorePropertyChangedObservable<T> : LightweightObservableBase<CorePropertyChangedEventArgs<T>>
    {
        private readonly CoreProperty<T> _property;
        private readonly ICoreObject _object;

        public CorePropertyChangedObservable(ICoreObject o, CoreProperty<T> property)
        {
            _object = o;
            _property = property;
        }

        private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e is CorePropertyChangedEventArgs<T> a && a.Property == _property)
            {
                PublishNext(a);
            }
        }

        protected override void Deinitialize()
        {
            _object.PropertyChanged -= Object_PropertyChanged;
        }

        protected override void Initialize()
        {
            _object.PropertyChanged += Object_PropertyChanged;
        }
    }
}
