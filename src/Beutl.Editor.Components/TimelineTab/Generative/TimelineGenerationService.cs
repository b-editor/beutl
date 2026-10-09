using System.Collections.ObjectModel;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.Generative;

/// <summary>
/// Runs the generations started from one scene's timeline and puts their results in place.
/// Jobs live as long as the editor; a popup is only a view of one.
/// </summary>
public sealed class TimelineGenerationService : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<TimelineGenerationService>();
    private readonly Scene _scene;
    private readonly HistoryManager _history;
    private readonly Func<IGenerativeNodeExecutor?> _executor;
    private readonly TimelineGenerationApplier _applier;
    private readonly IElementSourceHandlerRegistration? _handlerRegistration;
    private bool _disposed;

    public TimelineGenerationService(
        Scene scene,
        HistoryManager history,
        IElementAdder adder,
        Func<IGenerativeNodeExecutor?> executor,
        Func<string> resourceDirectory,
        ITimelineMediaProbe? probe = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        ArgumentNullException.ThrowIfNull(adder);
        ArgumentNullException.ThrowIfNull(resourceDirectory);
        _applier = new TimelineGenerationApplier(scene, history, adder, resourceDirectory, probe ?? TimelineMediaProbe.Instance);
        // Results are added through the editor's adder, which needs to know how to add them. The
        // handler keeps no state, so one registered by another service on the adder serves too.
        string sourceType = typeof(GeneratedElementsSource).AssemblyQualifiedName!;
        if (!adder.SourceHandlers.Handlers.Any(handler => handler.SourceTypeName == sourceType))
        {
            _handlerRegistration = adder.SourceHandlers.Register(
                new ElementSourceHandlerRegistration(new GeneratedElementsSourceHandler(), order: 30));
        }
    }

    public ObservableCollection<TimelineGenerationJob> Jobs { get; } = [];

    /// <summary>Starts setting up a generation; its placeholder shows straight away.</summary>
    public TimelineGenerationJob Create(
        TimelineGenerationTarget target,
        TimelineGenerationSpec spec,
        TimelineGenerationInputs inputs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var job = new TimelineGenerationJob(target, spec, inputs);
        job.Slot.Value = PlanSlot(job, spec);
        job.Spec.Subscribe(value =>
        {
            if (job.State.Value != TimelineGenerationState.Running)
                job.Slot.Value = PlanSlot(job, value);
        });
        Jobs.Add(job);
        return job;
    }

    /// <summary>
    /// Runs the job and places its result. A failure stays on the job for the person to
    /// read, and a cancelled job returns to its draft.
    /// </summary>
    public async Task RunAsync(TimelineGenerationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (job.State.Value == TimelineGenerationState.Running || !Jobs.Contains(job))
            return;

        using var cancellation = new CancellationTokenSource();
        job.Cancellation = cancellation;
        job.State.Value = TimelineGenerationState.Running;
        job.Error.Value = null;
        job.Status.Value = null;
        job.StartedAt = DateTimeOffset.Now;
        TimelineGenerationSpec spec = job.Spec.Value;
        TimelineGenerationInputs inputs = job.Inputs.Value;
        CancellationToken token = cancellation.Token;
        try
        {
            GenerativeRequest request = await Task.Run(
                () => TimelineGenerationRequests.Build(spec, inputs, job.RequestKeySeed),
                token);
            // Looked up per run: the API clients belong to the main window, which can come later.
            IGenerativeNodeExecutor executor = _executor()
                ?? throw new GenerativeExecutionException(NodeGraphStrings.Generative_ExecutorUnavailable);
            var progress = new StatusProgress(job, SynchronizationContext.Current);
            GenerativeExecutionResult result;
            try
            {
                result = await executor.ExecuteAsync(request, progress, token);
            }
            finally
            {
                progress.Close();
            }

            // Paid for and collected: the next run of this job is a new request.
            job.RequestKeySeed = Guid.NewGuid().ToString("N");
            job.Status.Value = Strings.AiAddingResultToTimeline;
            await _applier.ApplyAsync(job.Target, spec, inputs, request, result, job.Slot.Value, ReservedSlots(job), token);
            Remove(job);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_disposed)
            {
                job.State.Value = TimelineGenerationState.Draft;
                job.Status.Value = null;
            }
        }
        catch (GenerativeExecutionException ex)
        {
            if (ex.SettledRequest)
                job.RequestKeySeed = Guid.NewGuid().ToString("N");
            Fail(job, ex.Message);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "A timeline generation failed.");
            Fail(job, Strings.AiUnexpectedError);
        }
        finally
        {
            job.Cancellation = null;
        }
    }

    /// <summary>Shows another take of a generated element, as one undoable edit.</summary>
    public void SelectTake(Element element, ElementGenerationTake take)
        => TimelineGenerationApplier.SelectTake(_scene, _history, element, take);

    public void Cancel(TimelineGenerationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Cancellation?.Cancel();
    }

    /// <summary>Drops a job and its placeholder, stopping it if it is running.</summary>
    public void Remove(TimelineGenerationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (Jobs.Remove(job))
            job.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (TimelineGenerationJob job in Jobs.ToArray())
            job.Dispose();
        Jobs.Clear();
        _ = _handlerRegistration?.DisposeAsync().AsTask();
    }

    // Where the placeholder goes while the job is set up or running.
    internal TimelineGenerationSlot? PlanSlot(TimelineGenerationJob job, TimelineGenerationSpec spec)
    {
        TimelineGenerationTarget target = job.Target;
        TimeSpan length = TimeSpan.FromSeconds(Math.Max(1, spec.DurationSeconds));
        IEnumerable<TimelineGenerationSlot> reserved = ReservedSlots(job);
        switch (target.Placement)
        {
            case TimelineGenerationPlacement.Gap when target.Gap is { } gap:
                if (gap.MaxLength is { } max && max < length)
                    length = max;
                return new TimelineGenerationSlot(gap.Layer, new TimeRange(gap.Start, length));

            case TimelineGenerationPlacement.After when target.Source is { } source:
                {
                    var range = new TimeRange(source.Range.End, length);
                    int layer = TimelineGenerationSlots.IsFree(_scene, source.ZIndex, range, reserved)
                        ? source.ZIndex
                        : TimelineGenerationSlots.FindFreeLayer(_scene, range, source.ZIndex + 1, reserved: reserved)
                          ?? source.ZIndex;
                    return new TimelineGenerationSlot(layer, range);
                }

            case TimelineGenerationPlacement.Above when target.Source is { } source:
                {
                    int layer = TimelineGenerationSlots.FindFreeLayer(_scene, source.Range, source.ZIndex + 1, reserved: reserved)
                        ?? source.ZIndex + 1;
                    return new TimelineGenerationSlot(layer, source.Range);
                }

            case TimelineGenerationPlacement.ReplaceImage or TimelineGenerationPlacement.NewTake when target.Source is { } source:
                return new TimelineGenerationSlot(source.ZIndex, source.Range);

            default:
                return null;
        }
    }

    private TimelineGenerationSlot[] ReservedSlots(TimelineGenerationJob except)
        => Jobs
            .Where(job => !ReferenceEquals(job, except) && job.Slot.Value is not null)
            .Where(job => job.Target.Placement is not (TimelineGenerationPlacement.ReplaceImage or TimelineGenerationPlacement.NewTake))
            .Select(job => job.Slot.Value!.Value)
            .ToArray();

    private void Fail(TimelineGenerationJob job, string message)
    {
        if (_disposed)
            return;
        job.State.Value = TimelineGenerationState.Failed;
        job.Status.Value = null;
        job.Error.Value = message;
    }

    // Passes status text on and drops the preview pictures, which the job has no use for.
    private sealed class StatusProgress(TimelineGenerationJob job, SynchronizationContext? context)
        : IProgress<GenerativeProgress>
    {
        private volatile bool _closed;

        public void Report(GenerativeProgress value)
        {
            value.Preview?.Dispose();
            if (_closed || value.Status is not { } status)
                return;
            if (context is null)
            {
                job.Status.Value = status;
                return;
            }

            context.Post(_ =>
            {
                if (!_closed)
                    job.Status.Value = status;
            }, null);
        }

        public void Close() => _closed = true;
    }
}
