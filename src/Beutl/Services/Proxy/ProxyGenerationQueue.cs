using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.Proxy;

public sealed class ProxyGenerationQueue : IDisposable
{
    private static readonly Lazy<ProxyGenerationQueue> s_instance = new(() => new ProxyGenerationQueue());
    private static readonly ILogger s_logger = Log.CreateLogger<ProxyGenerationQueue>();

    private readonly Channel<ProxyJob> _channel = Channel.CreateUnbounded<ProxyJob>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });
    private readonly ConcurrentDictionary<string, ProxyJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Subject<ProxyJob> _jobSubject = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = [];
    private bool _disposed;

    public static ProxyGenerationQueue Instance => s_instance.Value;

    public IObservable<ProxyJob> JobChanged => _jobSubject;

    private ProxyGenerationQueue()
    {
        int workers = Math.Max(1, GlobalConfiguration.Instance.ProxyConfig.MaxParallelJobs);
        for (int i = 0; i < workers; i++)
        {
            _workers.Add(Task.Run(() => RunWorkerAsync(_cts.Token)));
        }
    }

    public ProxyJob Enqueue(string originalPath)
    {
        return Enqueue(originalPath, GlobalConfiguration.Instance.ProxyConfig.ActivePreset);
    }

    public ProxyJob Enqueue(string originalPath, ProxyPresetKind preset)
    {
        string key = Path.GetFullPath(originalPath);

        var job = _jobs.AddOrUpdate(
            key,
            _ => new ProxyJob(originalPath, preset),
            (_, existing) =>
            {
                if (existing.State is ProxyJobState.Pending or ProxyJobState.Running)
                    return existing;
                return new ProxyJob(originalPath, preset);
            });

        if (job.State is ProxyJobState.Pending or ProxyJobState.Running)
        {
            // 既存ジョブが未着手/進行中なら、enqueue は skip する
            return job;
        }

        _channel.Writer.TryWrite(job);
        PublishChange(job);
        return job;
    }

    public void CancelAll()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
    }

    private async Task RunWorkerAsync(CancellationToken token)
    {
        var generator = new ProxyGenerator();
        await foreach (var job in _channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            if (token.IsCancellationRequested) break;

            job.State = ProxyJobState.Running;
            job.Progress = 0;
            PublishChange(job);

            try
            {
                var progress = new Progress<double>(p =>
                {
                    job.Progress = p;
                    PublishChange(job);
                });

                var result = await generator.GenerateAsync(job.OriginalPath, job.Preset, progress, token).ConfigureAwait(false);

                if (result.Success)
                {
                    job.State = ProxyJobState.Completed;
                    job.Progress = 1.0;
                }
                else
                {
                    job.State = token.IsCancellationRequested ? ProxyJobState.Cancelled : ProxyJobState.Failed;
                    job.ErrorMessage = result.ErrorMessage;
                }
            }
            catch (OperationCanceledException)
            {
                job.State = ProxyJobState.Cancelled;
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Unhandled error in proxy generation worker.");
                job.State = ProxyJobState.Failed;
                job.ErrorMessage = ex.Message;
            }

            PublishChange(job);
        }
    }

    private void PublishChange(ProxyJob job)
    {
        try { _jobSubject.OnNext(job); }
        catch (Exception ex) { s_logger.LogDebug(ex, "ProxyJob subscriber threw."); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { /* ignore */ }
        _channel.Writer.TryComplete();
        try { Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _jobSubject.Dispose();
        _cts.Dispose();
    }
}
