using System.Collections.Concurrent;
using System.Diagnostics;

namespace Beutl.AgentHost;

public enum AgentAiJobStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>What an AI job produced: a file, or for a transcription, the transcript.</summary>
public sealed record AgentAiJobOutput(
    string? OutputPath,
    string? MediaKind,
    string? ModelId,
    int? Seed,
    AgentTranscript? Transcript = null);

public sealed record AgentAiJobSnapshot(
    string JobId,
    string Operation,
    AgentAiJobStatus Status,
    string? StatusText,
    double ElapsedSeconds,
    AgentAiJobOutput? Output,
    string? ErrorCode,
    string? ErrorMessage)
{
    public string? NextStep => Status switch
    {
        AgentAiJobStatus.Running => "Call read_ai_job(jobId, waitSeconds) to wait for the result; generation can take minutes.",
        AgentAiJobStatus.Succeeded when Output?.OutputPath is not null =>
            "The file is saved next to the open scene. Place it with apply_edit (SourceImage.Source or SourceVideo.Source set to the file), or use it as an input to another AI tool.",
        _ => null,
    };
}

/// <summary>
/// AI generations started by agents. They run for minutes, longer than an MCP request should
/// wait, so a tool starts a job, waits a while for it, and the agent reads it back by id.
/// </summary>
internal sealed class AgentAiJobManager : IDisposable
{
    // As many finished jobs as the render job manager keeps; running ones are never dropped.
    internal const int RetainedFinishedJobs = 128;
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _disposed;

    public string Start(
        string operation,
        Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> run)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(run);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var job = new Job(Guid.NewGuid().ToString("N"), operation, Interlocked.Increment(ref _sequence));
        _jobs[job.Id] = job;
        Prune();
        job.Task = Task.Run(() => RunAsync(job, run));
        return job.Id;
    }

    public AgentAiJobSnapshot? Get(string jobId)
        => _jobs.TryGetValue(jobId, out Job? job) ? job.Snapshot() : null;

    /// <summary>The job once it has finished or <paramref name="wait"/> has passed, whichever is first.</summary>
    public async Task<AgentAiJobSnapshot?> WaitAsync(string jobId, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out Job? job))
            return null;
        if (wait > TimeSpan.Zero && job.Task is { IsCompleted: false } task)
        {
            try
            {
                await task.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        return job.Snapshot();
    }

    public bool Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out Job? job))
            return false;
        job.Cancellation.Cancel();
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (Job job in _jobs.Values)
            job.Cancellation.Cancel();
    }

    private void Prune()
    {
        Job[] finished = _jobs.Values.Where(job => job.IsFinished).OrderBy(job => job.Sequence).ToArray();
        for (int index = 0; index < finished.Length - RetainedFinishedJobs; index++)
            _jobs.TryRemove(finished[index].Id, out _);
    }

    private static async Task RunAsync(Job job, Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> run)
    {
        try
        {
            AgentAiJobOutput output = await run(new Progress(job), job.Cancellation.Token).ConfigureAwait(false);
            job.Finish(AgentAiJobStatus.Succeeded, output, null, null);
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            job.Finish(AgentAiJobStatus.Cancelled, null, null, "The job was cancelled. A request the service had already accepted is still charged; its result can be collected from the AI tab's job history.");
        }
        catch (AgentAiException ex)
        {
            job.Finish(AgentAiJobStatus.Failed, null, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            job.Finish(AgentAiJobStatus.Failed, null, Beutl.AgentToolkit.Common.ErrorCode.AiGenerationFailed, ex.Message);
        }
    }

    private sealed class Progress(Job job) : IProgress<string>
    {
        public void Report(string value) => job.SetStatus(value);
    }

    private sealed class Job(string id, string operation, long sequence)
    {
        private readonly object _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AgentAiJobStatus _status = AgentAiJobStatus.Running;
        private string? _statusText;
        private AgentAiJobOutput? _output;
        private string? _errorCode;
        private string? _errorMessage;
        private TimeSpan? _elapsed;

        public string Id { get; } = id;

        public long Sequence { get; } = sequence;

        public bool IsFinished
        {
            get
            {
                lock (_gate)
                    return _status != AgentAiJobStatus.Running;
            }
        }

        public CancellationTokenSource Cancellation { get; } = new();

        public Task? Task { get; set; }

        public void SetStatus(string text)
        {
            lock (_gate)
            {
                if (_status == AgentAiJobStatus.Running)
                    _statusText = text;
            }
        }

        public void Finish(AgentAiJobStatus status, AgentAiJobOutput? output, string? errorCode, string? errorMessage)
        {
            lock (_gate)
            {
                _status = status;
                _statusText = null;
                _output = output;
                _errorCode = errorCode;
                _errorMessage = errorMessage;
                _elapsed = _clock.Elapsed;
            }
        }

        public AgentAiJobSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new AgentAiJobSnapshot(
                    Id,
                    operation,
                    _status,
                    _statusText,
                    Math.Round((_elapsed ?? _clock.Elapsed).TotalSeconds, 1),
                    _output,
                    _errorCode,
                    _errorMessage);
            }
        }
    }
}

/// <summary>A failure an AI tool reports with its own error code and a message for the agent.</summary>
internal sealed class AgentAiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
