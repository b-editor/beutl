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
        public object Sync { get; } = new();
        public RenderJobState State { get; set; } = RenderJobState.Running;
        public JsonNode? Result { get; set; }
        public Exception? Failure { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public string Enqueue(string kind, Func<CancellationToken, Task<JsonNode>> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string jobId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var record = new JobRecord
        {
            JobId = jobId,
            Kind = kind,
            StartedAt = DateTimeOffset.UtcNow,
            Cts = new CancellationTokenSource()
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
        try
        {
            await _gate.WaitAsync(record.Cts.Token).ConfigureAwait(false);
            acquired = true;
            JsonNode result = await work(record.Cts.Token).ConfigureAwait(false);
            lock (record.Sync)
            {
                record.Result = result;
                record.State = RenderJobState.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            lock (record.Sync)
            {
                record.State = RenderJobState.Cancelled;
            }
        }
        catch (Exception ex)
        {
            lock (record.Sync)
            {
                record.Failure = ex;
                record.State = RenderJobState.Failed;
            }
        }
        finally
        {
            lock (record.Sync)
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
            }

            if (acquired)
            {
                _gate.Release();
            }

            record.Cts.Dispose();
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
