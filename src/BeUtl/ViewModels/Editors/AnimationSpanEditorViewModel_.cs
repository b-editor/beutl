using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public interface IAnimationSpanEditorViewModel : IDisposable
{
    IAnimation Animation { get; }

    IAnimationSpan Model { get; }

    void RemoveItem();

    void Move(int newIndex, int oldIndex);

    void InsertForward(Easing easing);

    void InsertBackward(Easing easing);

    void SetEasing(Easing old, Easing @new);
}

public sealed class AnimationSpanEditorViewModel<T> : IAnimationSpanEditorViewModel
{
    private readonly Animation<T> _animation;

    public AnimationSpanEditorViewModel(AnimationSpan<T> model, Animation<T> animation)
    {
        Model = model;
        _animation = animation;

        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new AnimationSpanPropertyWrapper<T>(model, animation, true)));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new AnimationSpanPropertyWrapper<T>(model, animation, false)));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new CorePropertyWrapper<TimeSpan>(AnimationSpan.DurationProperty, model)));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new CorePropertyWrapper<Easing>(AnimationSpan.EasingProperty, model)));

        Header = model.GetObservable(AnimationSpan.EasingProperty)
            .Select(x => x.GetType().Name)
            .ToReadOnlyReactivePropertySlim(model.Easing.GetType().Name);
    }

    public ReadOnlyReactivePropertySlim<string> Header { get; }

    public AnimationSpan<T> Model { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<BaseEditorViewModel?> Properties { get; } = new();

    IAnimation IAnimationSpanEditorViewModel.Animation => _animation;

    IAnimationSpan IAnimationSpanEditorViewModel.Model => Model;

    public void RestoreState(JsonNode json)
    {
        try
        {
            IsExpanded.Value = (bool?)json["is-expanded"] ?? true;
        }
        catch
        {
        }
    }

    public JsonNode SaveState()
    {
        return new JsonObject
        {
            ["is-expanded"] = IsExpanded.Value
        };
    }

    public void Dispose()
    {
        foreach (BaseEditorViewModel? item in Properties.AsSpan())
        {
            item?.Dispose();
        }

        Header.Dispose();
    }

    public void RemoveItem()
    {
        new RemoveCommand(_animation.Children, Model)
            .DoAndRecord(CommandRecorder.Default);
    }

    public void Move(int newIndex, int oldIndex)
    {
        new MoveCommand(_animation.Children, newIndex, oldIndex).DoAndRecord(CommandRecorder.Default);
    }

    private void InsertItem(int index, AnimationSpan<T> item)
    {
        new AddCommand(_animation.Children, item, index).DoAndRecord(CommandRecorder.Default);
    }

    public void InsertForward(Easing easing)
    {
        int index = _animation.Children.IndexOf(Model);
        CoreProperty<T> property = _animation.Property;
        Type type = typeof(AnimationSpan<>).MakeGenericType(property.PropertyType);
        Type ownerType = property.OwnerType;
        ILogicalElement? owner = _animation.FindLogicalParent(ownerType);
        T? defaultValue = default;
        bool hasDefaultValue = true;
        if (owner is ICoreObject ownerCO)
        {
            defaultValue = ownerCO.GetValue(property);
        }
        else if (owner != null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(owner.GetType());
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }
        else
        {
            hasDefaultValue = false;
        }

        if (Activator.CreateInstance(type) is AnimationSpan<T> animation)
        {
            animation.Easing = easing;
            animation.Duration = TimeSpan.FromSeconds(2);

            if (hasDefaultValue && defaultValue != null)
            {
                animation.Previous = defaultValue;
                animation.Next = defaultValue;
            }

            InsertItem(index, animation);
        }
    }

    public void InsertBackward(Easing easing)
    {
        int index = _animation.Children.IndexOf(Model);
        CoreProperty<T> property = _animation.Property;
        Type type = typeof(AnimationSpan<>).MakeGenericType(property.PropertyType);
        Type ownerType = property.OwnerType;
        ILogicalElement? owner = _animation.FindLogicalParent(ownerType);
        T? defaultValue = default;
        bool hasDefaultValue = true;
        if (owner is ICoreObject ownerCO)
        {
            defaultValue = ownerCO.GetValue(property);
        }
        else if (owner != null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(owner.GetType());
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }
        else
        {
            hasDefaultValue = false;
        }

        if (Activator.CreateInstance(type) is AnimationSpan<T> animation)
        {
            animation.Easing = easing;
            animation.Duration = TimeSpan.FromSeconds(2);

            if (hasDefaultValue && defaultValue != null)
            {
                animation.Previous = defaultValue;
                animation.Next = defaultValue;
            }

            InsertItem(index + 1, animation);
        }
    }

    public void SetEasing(Easing old, Easing @new)
    {
        new ChangePropertyCommand<Easing>(Model, AnimationSpan.EasingProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }
}
