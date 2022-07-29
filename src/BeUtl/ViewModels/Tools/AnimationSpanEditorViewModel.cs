using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class AnimationSpanEditorViewModel : IDisposable
{
    public AnimationSpanEditorViewModel(IAnimationSpan model, IWrappedProperty.IAnimatable property)
    {
        Model = model;
        WrappedProperty = property;

        (IWrappedProperty prev, IWrappedProperty next) = property.CreateSpanWrapper(model);
        Properties.Add(PropertyEditorService.CreateEditorViewModel(prev));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(next));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new CorePropertyWrapper<TimeSpan>(AnimationSpan.DurationProperty, model)));
        Properties.Add(PropertyEditorService.CreateEditorViewModel(
            new CorePropertyWrapper<Easing>(AnimationSpan.EasingProperty, model)));

        Header = model.GetObservable(AnimationSpan.EasingProperty)
            .Select(x => x.GetType().Name)
            .ToReadOnlyReactivePropertySlim(model.Easing.GetType().Name);
    }

    public ReadOnlyReactivePropertySlim<string> Header { get; }

    public IWrappedProperty.IAnimatable WrappedProperty { get; }

    public IAnimationSpan Model { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<BaseEditorViewModel?> Properties { get; } = new();

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
        WrappedProperty.Remove(Model);
    }

    public void Move(int newIndex, int oldIndex)
    {
        WrappedProperty.Move(newIndex, oldIndex);
    }

    public void InsertForward(Easing easing)
    {
        int index = WrappedProperty.IndexOf(Model);

        IAnimationSpan item = WrappedProperty.CreateSpan(easing);
        WrappedProperty.Insert(index, item);
    }

    public void InsertBackward(Easing easing)
    {
        int index = WrappedProperty.IndexOf(Model);

        IAnimationSpan item = WrappedProperty.CreateSpan(easing);
        WrappedProperty.Insert(index + 1, item);
    }

    public void SetEasing(Easing old, Easing @new)
    {
        new ChangePropertyCommand<Easing>(Model, AnimationSpan.EasingProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }
}
