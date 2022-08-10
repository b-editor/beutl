using System.Collections;
using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.Framework;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class AnimationTabViewModel : IToolContext
{
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;

    public AnimationTabViewModel()
    {
        Header = S.Common.AnimationObservable.ToReadOnlyReactivePropertySlim()!;

        _disposable0 = Animation.Subscribe(animation =>
        {
            ClearItems();
            if (animation != null)
            {
                _disposable1?.Dispose();
                _disposable1 = animation.Animation.Children.ForEachItem(
                    (idx, item) => Items.Insert(idx, new AnimationSpanEditorViewModel(item, animation)),
                    (idx, _) =>
                    {
                        Items[idx]?.Dispose();
                        Items.RemoveAt(idx);
                    },
                    () => ClearItems());
            }
        });
    }

    public Action<IAnimationSpan>? RequestScroll { get; set; }

    public ReactiveProperty<IAbstractAnimatableProperty?> Animation { get; } = new();

    public CoreList<AnimationSpanEditorViewModel?> Items { get; } = new();

    public ToolTabExtension Extension => AnimationTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void ScrollTo(IAnimationSpan obj)
    {
        RequestScroll?.Invoke(obj);
    }

    public void Dispose()
    {
        _disposable0.Dispose();
        _disposable1?.Dispose();
        ClearItems();

        Animation.Dispose();
        Header.Dispose();
    }

    private void ClearItems()
    {
        foreach (AnimationSpanEditorViewModel? item in Items.GetMarshal().Value)
        {
            item?.Dispose();
        }
        Items.Clear();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }

    public void AddAnimation(Easing easing)
    {
        if (Animation.Value?.Animation is not IAnimation animation
            || animation.Children is not IList list)
        {
            return;
        }

        CoreProperty property = animation.Property;
        Type type = typeof(AnimationSpan<>).MakeGenericType(property.PropertyType);
        Type ownerType = property.OwnerType;
        ILogicalElement? owner = animation.FindLogicalParent(ownerType);
        object? defaultValue = null;
        if (owner is ICoreObject ownerCO)
        {
            defaultValue = ownerCO.GetValue(property);
        }
        else if (owner != null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            defaultValue = property.GetMetadata<CorePropertyMetadata>(owner.GetType()).GetDefaultValue();
        }

        if (Activator.CreateInstance(type) is IAnimationSpan animationSpan)
        {
            animationSpan.Easing = easing;
            animationSpan.Duration = TimeSpan.FromSeconds(2);

            if (defaultValue != null)
            {
                animationSpan.Previous = defaultValue;
                animationSpan.Next = defaultValue;
            }

            var command = new AddCommand(list, animationSpan, animation.Children.Count);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }
}
