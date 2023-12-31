using Beutl.Animation;
using Beutl.Commands;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineKeyFrameViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly InlineAnimationLayerViewModel _parent;

    public InlineKeyFrameViewModel(IKeyFrame keyframe, IKeyFrameAnimation animation, InlineAnimationLayerViewModel parent)
    {
        Model = keyframe;
        Animation = animation;
        Timeline = parent.Timeline;
        _parent = parent;

        Left = keyframe.GetObservable(KeyFrame.KeyTimeProperty)
            .CombineLatest(Timeline.Options)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        RemoveCommand.Subscribe(() =>
            {
                Animation.KeyFrames.BeginRecord<IKeyFrame>()
                    .Remove(Model)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            })
            .DisposeWith(_disposables);
    }

    public IKeyFrame Model { get; }

    public IKeyFrameAnimation Animation { get; }

    public TimelineViewModel Timeline { get; }

    public ReactiveProperty<double> Left { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public void UpdateKeyTime()
    {
        float scale = Timeline.Options.Value.Scale;
        Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan time = Left.Value.ToTimeSpan(scale).RoundToRate(rate);
        new ChangePropertyCommand<TimeSpan>(Model, KeyFrame.KeyTimeProperty, time, Model.KeyTime)
            .DoAndRecord(CommandRecorder.Default);

        Left.Value = time.ToPixel(scale);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
