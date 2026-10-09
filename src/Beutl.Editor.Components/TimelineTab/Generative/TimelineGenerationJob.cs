using Beutl.Editor.Services.AI;
using Beutl.Media;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.Editor.Components.TimelineTab.Generative;

/// <summary>Where a timeline generation's result goes.</summary>
public enum TimelineGenerationPlacement
{
    /// <summary>Right after the source clip: a clip made from it, or its continuation.</summary>
    After,

    /// <summary>On a free layer above the source clip, over the same stretch of time.</summary>
    Above,

    /// <summary>Into the source picture's place, when the result keeps its shape.</summary>
    ReplaceImage,

    /// <summary>Into an empty stretch of a layer.</summary>
    Gap,

    /// <summary>As another take of a generated element, which then shows it.</summary>
    NewTake,
}

/// <summary>What a generation starts from: the clip acted on, or the gap filled.</summary>
public sealed record TimelineGenerationTarget(
    TimelineGenerationPlacement Placement,
    Element? Source = null,
    TimelineGap? Gap = null)
{
    public static TimelineGenerationTarget ForElement(TimelineGenerationPlacement placement, Element source)
        => new(placement, source ?? throw new ArgumentNullException(nameof(source)));

    public static TimelineGenerationTarget ForGap(TimelineGap gap)
        => new(TimelineGenerationPlacement.Gap, Gap: gap ?? throw new ArgumentNullException(nameof(gap)));
}

public enum TimelineGenerationState
{
    /// <summary>Being set up in the popup; nothing has been sent.</summary>
    Draft,

    Running,

    /// <summary>Stopped with an error the person can read; it can be run again.</summary>
    Failed,
}

/// <summary>
/// One generation started from the timeline. It shows as a placeholder where its result
/// will go from the moment the popup opens until the result is placed.
/// </summary>
public sealed class TimelineGenerationJob : IDisposable
{
    internal TimelineGenerationJob(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs)
    {
        Target = target;
        Spec = new ReactivePropertySlim<TimelineGenerationSpec>(spec);
        Inputs = new ReactivePropertySlim<TimelineGenerationInputs>(inputs);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public TimelineGenerationTarget Target { get; }

    public ReactivePropertySlim<TimelineGenerationSpec> Spec { get; }

    public ReactivePropertySlim<TimelineGenerationInputs> Inputs { get; }

    public ReactivePropertySlim<TimelineGenerationState> State { get; } = new(TimelineGenerationState.Draft);

    /// <summary>What the running generation last reported, such as waiting in the queue.</summary>
    public ReactivePropertySlim<string?> Status { get; } = new();

    public ReactivePropertySlim<string?> Error { get; } = new();

    /// <summary>True while what the job starts from is still being prepared, such as a captured frame.</summary>
    public ReactivePropertySlim<bool> IsPreparing { get; } = new();

    /// <summary>Where the placeholder sits, or null when the result replaces the source in place.</summary>
    public ReactivePropertySlim<TimelineGenerationSlot?> Slot { get; } = new();

    public DateTimeOffset? StartedAt { get; internal set; }

    /// <summary>
    /// Names the paid request. Kept while a request may still be collected, and renewed once
    /// it has settled, so the next run is a new request rather than the old answer.
    /// </summary>
    internal string RequestKeySeed { get; set; } = Guid.NewGuid().ToString("N");

    internal CancellationTokenSource? Cancellation { get; set; }

    public void Dispose()
    {
        Cancellation?.Cancel();
        Cancellation?.Dispose();
        Cancellation = null;
        Spec.Dispose();
        Inputs.Dispose();
        State.Dispose();
        Status.Dispose();
        Error.Dispose();
        IsPreparing.Dispose();
        Slot.Dispose();
    }
}
