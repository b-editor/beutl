using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.AgentHost;

// Written by name, as the contract documents it; the Web serializer defaults would emit an ordinal.
[JsonConverter(typeof(JsonStringEnumConverter<AgentAiJobStatus>))]
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
    private static readonly ILogger s_logger = Log.CreateLogger<AgentAiJobManager>();
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    // Admission and disposal share it, so no job is added after Dispose has cancelled the others.
    private readonly object _lifetimeSync = new();
    private long _sequence;
    private bool _disposed;

    public string Start(
        string operation,
        Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> run)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(run);
        var job = new Job(Guid.NewGuid().ToString("N"), operation, Interlocked.Increment(ref _sequence));
        lock (_lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs[job.Id] = job;
            job.Task = Task.Run(() => RunAsync(job, run));
        }

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
        lock (_lifetimeSync)
        {
            _disposed = true;
            foreach (Job job in _jobs.Values)
                job.Cancellation.Cancel();
        }
    }

    private void Prune()
    {
        Job[] finished = _jobs.Values.Where(job => job.IsFinished).OrderBy(job => job.Sequence).ToArray();
        for (int index = 0; index < finished.Length - RetainedFinishedJobs; index++)
            _jobs.TryRemove(finished[index].Id, out _);
    }

    private async Task RunAsync(Job job, Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> run)
    {
        await RunCoreAsync(job, run).ConfigureAwait(false);
        // Pruned as each job finishes, so the bound holds even when no later job starts.
        Prune();
    }

    private static async Task RunCoreAsync(Job job, Func<IProgress<string>, CancellationToken, Task<AgentAiJobOutput>> run)
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
            // An unexpected message can carry local paths or service details; it stays in the log.
            s_logger.LogError(ex, "AI job {Operation} failed unexpectedly.", job.Operation);
            job.Finish(
                AgentAiJobStatus.Failed,
                null,
                Beutl.AgentToolkit.Common.ErrorCode.AiGenerationFailed,
                $"The AI job failed unexpectedly ({ex.GetType().Name}). The details are in the Beutl app's log.");
        }
    }

    private sealed class Progress(Job job) : IProgress<string>
    {
        public void Report(string value) => job.SetStatus(value);
    }

    private sealed class Job(string id, string operation, long sequence)
    {
        public string Operation { get; } = operation;

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
                    Operation,
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
