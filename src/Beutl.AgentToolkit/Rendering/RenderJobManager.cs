using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Rendering;

public enum RenderJobState
{
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record RenderJobSnapshot(
    string JobId,
    string Kind,
    string State,
    JsonNode? Result,
    ToolError? Error,
    string StartedAt,
    string? CompletedAt);

// Background render/export jobs so a long render is not killed by the MCP client request timeout.
// Jobs are serialized (single-flight) because all stills share the one RenderThread and each export
// builds its own graphics context; concurrent background renders would race those resources.
public sealed class RenderJobManager : IDisposable
{
    private sealed class JobRecord
    {
        public required string JobId { get; init; }
        public required string Kind { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required IDisposable OutputOperationLease { get; init; }
        public object Sync { get; } = new();
        public RenderJobState State { get; set; } = RenderJobState.Running;
        public JsonNode? Result { get; set; }
        public Exception? Failure { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Queues a background render or export and transfers the output-operation lease to the job.
    /// </summary>
    /// <param name="kind">The job kind reported in snapshots.</param>
    /// <param name="work">The asynchronous work to run under the single-flight gate.</param>
    /// <param name="outputOperationLease">
    /// The lease to hold through the terminal path. The caller retains ownership when this method
    /// throws; after a successful return, the job owns and releases it exactly once.
    /// </param>
    /// <returns>The generated job identifier.</returns>
    public string Enqueue(
        string kind,
        Func<CancellationToken, Task<JsonNode>> work,
        IDisposable outputOperationLease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(outputOperationLease);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string jobId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var record = new JobRecord
        {
            JobId = jobId,
            Kind = kind,
            StartedAt = DateTimeOffset.UtcNow,
            Cts = new CancellationTokenSource(),
            OutputOperationLease = outputOperationLease
        };
        _jobs[jobId] = record;
        _ = RunAsync(record, work);
        return jobId;
    }

    public RenderJobSnapshot? Get(string jobId)
    {
        if (jobId is null || !_jobs.TryGetValue(jobId, out JobRecord? record))
        {
            return null;
        }

        lock (record.Sync)
        {
            ToolError? error = record.Failure is null ? null : ToolErrorMapper.Map(record.Failure);
            return new RenderJobSnapshot(
                record.JobId,
                record.Kind,
                StateToString(record.State),
                record.Result?.DeepClone(),
                error,
                record.StartedAt.ToString("O"),
                record.CompletedAt?.ToString("O"));
        }
    }

    public bool Cancel(string jobId)
    {
        if (jobId is null || !_jobs.TryGetValue(jobId, out JobRecord? record))
        {
            return false;
        }

        lock (record.Sync)
        {
            if (record.State != RenderJobState.Running)
            {
                return false;
            }
        }

        try
        {
            record.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job finished (and RunAsync disposed the CTS) between the state check and here;
            // treat it like the not-running case instead of surfacing an internal error.
            return false;
        }

        return true;
    }

    public bool HasRunningJobs
    {
        get
        {
            foreach (JobRecord record in _jobs.Values)
            {
                lock (record.Sync)
                {
                    if (record.State == RenderJobState.Running)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private async Task RunAsync(JobRecord record, Func<CancellationToken, Task<JsonNode>> work)
    {
        bool acquired = false;
        RenderJobState terminalState = RenderJobState.Running;
        JsonNode? result = null;
        Exception? failure = null;
        try
        {
            await _gate.WaitAsync(record.Cts.Token).ConfigureAwait(false);
            acquired = true;
            result = await work(record.Cts.Token).ConfigureAwait(false);
            terminalState = RenderJobState.Completed;
        }
        catch (OperationCanceledException)
        {
            terminalState = RenderJobState.Cancelled;
        }
        catch (Exception ex)
        {
            failure = ex;
            terminalState = RenderJobState.Failed;
        }
        finally
        {
            try
            {
                record.OutputOperationLease.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
                terminalState = RenderJobState.Failed;
            }
            finally
            {
                if (acquired)
                {
                    try
                    {
                        _gate.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Dispose may race a job observing cancellation. The output lease is already
                        // released, and a disposed manager no longer needs the single-flight permit.
                    }
                }

                record.Cts.Dispose();
            }

            lock (record.Sync)
            {
                record.Result = terminalState == RenderJobState.Completed ? result : null;
                record.Failure = failure;
                record.State = terminalState;
                record.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static string StateToString(RenderJobState state)
    {
        return state switch
        {
            RenderJobState.Running => "running",
            RenderJobState.Completed => "completed",
            RenderJobState.Failed => "failed",
            RenderJobState.Cancelled => "cancelled",
            _ => "running"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (JobRecord record in _jobs.Values)
        {
            try
            {
                record.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _gate.Dispose();
    }
}
