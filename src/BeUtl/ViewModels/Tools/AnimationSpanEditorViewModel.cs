using System.Collections;
using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.Framework;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class AnimationSpanEditorViewModel : IDisposable
{
    public AnimationSpanEditorViewModel(IAnimationSpan model, IAbstractAnimatableProperty property)
    {
        static IPropertyEditorContext? CreateContext(IAbstractProperty[] property)
        {
            return PropertyEditorExtension.Instance.TryCreateContext(property, out IPropertyEditorContext? ctx) ? ctx : null;
        }

        Model = model;
        WrappedProperty = property;

        var tmp = new IAbstractProperty[1];
        (IAbstractProperty prev, IAbstractProperty next) = property.CreateSpanWrapper(model);

        tmp[0] = prev;
        Properties.Add(CreateContext(tmp));
        tmp[0] = next;
        Properties.Add(CreateContext(tmp));

        tmp[0] = new CorePropertyClientImpl<TimeSpan>(AnimationSpan.DurationProperty, model);
        Properties.Add(CreateContext(tmp));

        tmp[0] = new CorePropertyClientImpl<Easing>(AnimationSpan.EasingProperty, model);
        Properties.Add(CreateContext(tmp));

        Header = model.GetObservable(AnimationSpan.EasingProperty)
            .Select(x => x.GetType().Name)
            .ToReadOnlyReactivePropertySlim(model.Easing.GetType().Name);
    }

    public ReadOnlyReactivePropertySlim<string> Header { get; }

    public IAbstractAnimatableProperty WrappedProperty { get; }

    public IAnimationSpan Model { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<IPropertyEditorContext?> Properties { get; } = new();

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
        foreach (BaseEditorViewModel? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }

        Header.Dispose();
    }

    public void RemoveItem()
    {
        if (WrappedProperty.Animation is IList list)
        {
            list.BeginRecord()
                .Remove(Model)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (WrappedProperty.Animation is IList list)
        {
            list.BeginRecord()
                .Move(oldIndex, newIndex)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertForward(Easing easing)
    {
        if (WrappedProperty.Animation is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = WrappedProperty.CreateSpan(easing);
            list.BeginRecord()
                .Insert(index, item)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (WrappedProperty.Animation is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = WrappedProperty.CreateSpan(easing);
            list.BeginRecord()
                .Insert(index + 1, item)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SetEasing(Easing old, Easing @new)
    {
        new ChangePropertyCommand<Easing>(Model, AnimationSpan.EasingProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }
}
