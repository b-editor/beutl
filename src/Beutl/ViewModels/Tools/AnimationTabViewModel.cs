using System.Collections;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class AnimationTabViewModel : IToolContext
{
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;

    public AnimationTabViewModel()
    {
        Header = new ReactivePropertySlim<string>(Strings.Animation);

        _disposable0 = Animation.Subscribe(animation =>
        {
            _disposable1?.Dispose();
            ClearItems();
            if (animation is { Animation: IKeyFrameAnimation kfAnimation })
            {
                _disposable1 = kfAnimation.KeyFrames.ForEachItem(
                    (idx, item) => Items.Insert(idx, new AnimationSpanEditorViewModel(item, kfAnimation, animation)),
                    (idx, _) =>
                    {
                        Items[idx]?.Dispose();
                        Items.RemoveAt(idx);
                    },
                    () => ClearItems());
            }
        });
    }

    public Action<IKeyFrame>? RequestScroll { get; set; }

    public ReactiveProperty<IAbstractAnimatableProperty?> Animation { get; } = new();

    public CoreList<AnimationSpanEditorViewModel?> Items { get; } = new();

    public ToolTabExtension Extension => AnimationTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void ScrollTo(IKeyFrame obj)
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
        if (Animation.Value?.Animation is IKeyFrameAnimation animation
            && Activator.CreateInstance(typeof(KeyFrame<>).MakeGenericType(Animation.Value.Property.PropertyType)) is IKeyFrame keyframe)
        {
            keyframe.Easing = easing;
            // Todo: new-animation/現在のフレームから
            keyframe.KeyTime = animation.Duration + TimeSpan.FromSeconds(2);
            animation.KeyFrames.BeginRecord<IKeyFrame>()
                .Add(keyframe)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }
}
