using System.Collections;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.Framework;
using Beutl.Operators.Configure;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class AnimationSpanEditorViewModel : IDisposable
{
    private readonly IKeyFrameAnimation _animation;

    public AnimationSpanEditorViewModel(IKeyFrame model, IKeyFrameAnimation animation, IAbstractAnimatableProperty property)
    {
        static IPropertyEditorContext? CreateContext(IAbstractProperty[] property)
        {
            return PropertyEditorExtension.Instance.TryCreateContext(property, out IPropertyEditorContext? ctx) ? ctx : null;
        }

        Model = model;
        _animation = animation;
        WrappedProperty = property;

        var tmp = new IAbstractProperty[1];

        tmp[0] = property.CreateKeyFrameProperty(animation, model);
        Properties.Add(CreateContext(tmp));

        tmp[0] = new CorePropertyImpl<TimeSpan>(KeyFrame.KeyTimeProperty, model);
        Properties.Add(CreateContext(tmp));

        tmp[0] = new CorePropertyImpl<Easing>(KeyFrame.EasingProperty, model);
        Properties.Add(CreateContext(tmp));

        Header = model.GetObservable(KeyFrame.EasingProperty)
            .Select(x => x.GetType().Name)
            .ToReadOnlyReactivePropertySlim(model.Easing.GetType().Name);
    }

    public ReadOnlyReactivePropertySlim<string> Header { get; }

    public IAbstractAnimatableProperty WrappedProperty { get; }

    public IKeyFrame Model { get; }

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
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }

        Header.Dispose();
    }

    public void RemoveItem()
    {
        _animation.KeyFrames.BeginRecord<IKeyFrame>()
            .Remove(Model)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public void SetEasing(Easing old, Easing @new)
    {
        new ChangePropertyCommand<Easing>(Model, KeyFrame.EasingProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }
}
