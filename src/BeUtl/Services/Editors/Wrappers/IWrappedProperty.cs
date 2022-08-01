using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;

namespace BeUtl.Services.Editors.Wrappers;

public interface IWrappedProperty
{
    object Tag { get; }

    IObservable<string> Header { get; }

    CoreProperty AssociatedProperty { get; }

    void SetValue(object? value);

    object? GetValue();

    IObservable<object?> GetObservable();

    public interface IAnimatable : IWrappedProperty
    {
        IAnimation Animation { get; }

        IObservable<bool> HasAnimation { get; }

        void Move(int newIndex, int oldIndex);

        (IWrappedProperty Previous, IWrappedProperty Next) CreateSpanWrapper(IAnimationSpan animationSpan);

        IAnimationSpan CreateSpan(Easing easing);

        int IndexOf(IAnimationSpan item);

        void Insert(int index, IAnimationSpan item);

        void Remove(IAnimationSpan item);
    }
}

public interface IWrappedProperty<T> : IWrappedProperty
{
    new IObservable<T?> GetObservable();

    void SetValue(T? value);

    new T? GetValue();

    new CoreProperty<T> AssociatedProperty { get; }

    void IWrappedProperty.SetValue(object? value)
    {
        if (value is T typed)
        {
            SetValue(typed);
        }
        else
        {
            SetValue(default);
        }
    }

    object? IWrappedProperty.GetValue()
    {
        return GetValue();
    }

    IObservable<object?> IWrappedProperty.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }

    CoreProperty IWrappedProperty.AssociatedProperty => AssociatedProperty;

    public new interface IAnimatable : IWrappedProperty<T>, IWrappedProperty.IAnimatable
    {
        new Animation<T> Animation { get; }

        IAnimation IWrappedProperty.IAnimatable.Animation => Animation;

        IAnimationSpan IWrappedProperty.IAnimatable.CreateSpan(Easing easing)
        {
            CoreProperty<T> property = AssociatedProperty;
            Type ownerType = property.OwnerType;
            ILogicalElement? owner = Animation.FindLogicalParent(ownerType);
            T? defaultValue = GetValue();
            bool hasDefaultValue = true;
            if (owner != null && defaultValue == null)
            {
                // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
                CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(owner.GetType());
                defaultValue = metadata.DefaultValue;
                hasDefaultValue = metadata.HasDefaultValue;
            }

            var span = new AnimationSpan<T>
            {
                Easing = easing,
                Duration = TimeSpan.FromSeconds(2)
            };

            if (hasDefaultValue && defaultValue != null)
            {
                span.Previous = defaultValue;
                span.Next = defaultValue;
            }

            return span;
        }

        int IWrappedProperty.IAnimatable.IndexOf(IAnimationSpan item)
        {
            return Animation.Children.IndexOf(item);
        }

        void IWrappedProperty.IAnimatable.Insert(int index, IAnimationSpan item)
        {
            new AddCommand(Animation.Children, item, index)
                .DoAndRecord(CommandRecorder.Default);
        }

        void IWrappedProperty.IAnimatable.Remove(IAnimationSpan item)
        {
            new RemoveCommand(Animation.Children, item)
                .DoAndRecord(CommandRecorder.Default);
        }

        void IWrappedProperty.IAnimatable.Move(int newIndex, int oldIndex)
        {
            new MoveCommand(Animation.Children, newIndex, oldIndex)
                .DoAndRecord(CommandRecorder.Default);
        }

        (IWrappedProperty Previous, IWrappedProperty Next) IWrappedProperty.IAnimatable.CreateSpanWrapper(IAnimationSpan animationSpan)
        {
            return (new AnimationSpanPropertyWrapper<T>((AnimationSpan<T>)animationSpan, Animation, true),
                new AnimationSpanPropertyWrapper<T>((AnimationSpan<T>)animationSpan, Animation, false));
        }
    }
}
