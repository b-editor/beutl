using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

/// <summary>
/// Shows where a timeline generation's result will go while it is set up and running, with
/// its progress; clicking it opens the generation's popup again.
/// </summary>
public sealed class GenerationPlaceholderViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public GenerationPlaceholderViewModel(TimelineGenerationJob job, TimelineTabViewModel timeline)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

        IObservable<TimelineGenerationSlot?> slot = job.Slot;
        Margin = slot
            .Select(value => value is { } s
                ? timeline.GetTrackedLayerTopObservable(s.Layer)
                    .CombineLatest(timeline.Scale, (top, scale) => new Thickness(s.Range.Start.TimeToPixel(scale), top, 0, 0))
                : Observable.Return(default(Thickness)))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Width = slot
            .CombineLatest(timeline.Scale, (value, scale) => value is { } s ? s.Range.Duration.TimeToPixel(scale) : 0d)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        IsVisible = slot
            .Select(value => value is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        IsRunning = job.State
            .Select(state => state == TimelineGenerationState.Running)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        IsFailed = job.State
            .Select(state => state == TimelineGenerationState.Failed)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Text = job.State
            .CombineLatest(
                job.Status,
                job.Error,
                job.Spec,
                Observable.Interval(TimeSpan.FromSeconds(1)).Select(_ => 0L).StartWith(0L),
                (state, status, error, spec, _) => state switch
                {
                    TimelineGenerationState.Running => Running(status),
                    TimelineGenerationState.Failed => error ?? Strings.AiTimelineGenerationFailed,
                    _ => Describe(spec),
                })
            .ObserveOnContext(SynchronizationContext.Current)
            .ToReadOnlyReactivePropertySlim(string.Empty)
            .DisposeWith(_disposables);
    }

    public TimelineGenerationJob Job { get; }

    public TimelineTabViewModel Timeline { get; }

    public ReadOnlyReactivePropertySlim<Thickness> Margin { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<bool> IsVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRunning { get; }

    public ReadOnlyReactivePropertySlim<bool> IsFailed { get; }

    public ReadOnlyReactivePropertySlim<string> Text { get; }

    public void Dispose() => _disposables.Dispose();

    private string Running(string? status)
    {
        TimeSpan elapsed = Job.StartedAt is { } started ? DateTimeOffset.Now - started : TimeSpan.Zero;
        string time = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        return $"{status ?? Strings.AiTimelineGenerating} {time}";
    }

    private static string Describe(TimelineGenerationSpec spec)
    {
        string prompt = spec.Prompt.Trim();
        return prompt.Length != 0 ? prompt : TimelineAiPopupViewModel.TitleFor(spec);
    }
}
