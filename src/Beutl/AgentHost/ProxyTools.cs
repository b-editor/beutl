using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

[McpServerToolType]
internal sealed class ProxyTools(AgentSessionManager sessions, EditorService editor, GlobalConfiguration configuration,
    ProxyTools.JobHistory history) : ToolBase
{
    public sealed record JobInfo(Guid JobId, string MediaPath, string Preset, VideoDeinterlaceOptions Deinterlace,
        string Status, double? Progress, string? Message);
    public sealed record GenerationResponse(IReadOnlyList<JobInfo> Jobs, IReadOnlyList<string> UnavailablePaths);
    public sealed record ProxyInfo(string MediaPath, string Preset, VideoDeinterlaceOptions Deinterlace, string State,
        bool OriginalAvailable, string? FailureReason);
    public sealed record StatusResponse(IReadOnlyList<ProxyInfo> Proxies, IReadOnlyList<JobInfo> Jobs,
        IReadOnlyList<Guid> UnknownJobIds);
    public sealed record RemoveResponse(IReadOnlyList<ProxyInfo> Removed, IReadOnlyList<ProxyInfo> Failed,
        IReadOnlyList<Guid> CanceledJobIds);

    private sealed record Source(string Path, string Key, ProxyFingerprint? Fingerprint, VideoDeinterlaceOptions Deinterlace);

    private sealed record SourceSelection(
        IReadOnlyDictionary<(string Key, VideoDeinterlaceOptions Deinterlace), Source> ElementSources,
        IReadOnlyDictionary<string, Source> ExplicitPaths,
        ProxyPreset? Preset)
    {
        public bool Matches(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace)
            => (Preset is null || Preset == preset)
                && (ExplicitPaths.ContainsKey(source.AbsolutePath) || ElementSources.ContainsKey((source.AbsolutePath, deinterlace)));
    }

    [McpServerTool(Name = "generate_proxies")]
    [Description("Queues proxy generation in the running editor for selected video media. Returns job IDs immediately. Poll read_proxy_status with those IDs for progress and outcomes.")]
    public ValueTask<ToolResult<GenerationResponse>> GenerateProxies(
        [Description("Scene element IDs from read_document_summary. Supply elementIds, mediaPaths, or both.")] string[]? elementIds = null,
        [Description("Absolute paths to original video files.")] string[]? mediaPaths = null,
        [Description("Proxy preset: half, quarter, or eighth. Omit to use the editor's configured default.")] string? preset = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            RequireSelection(elementIds, mediaPaths);
            ProxyPreset quality = ReadPreset(preset) ?? (ProxyPreset)configuration.ProxyStoreConfig.DefaultPreset.Value;
            IEditingSession session = sessions.RequireSession();
            var (_, queue) = ReadServices(session);
            Source[] sources = ReadSources(session, elementIds, mediaPaths, includeAll: false);
            var jobs = new List<ProxyJob>();
            var unavailable = new HashSet<string>(StringComparer.Ordinal);
            foreach (Source source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.Fingerprint is not { } fingerprint)
                {
                    unavailable.Add(source.Path);
                    continue;
                }

                // Use the same foreground priority as explicitly generating a proxy in the tab.
                ProxyJob job = await queue.EnqueueAsync(fingerprint, quality, priority: 1,
                    cancellationToken, source.Deinterlace).ConfigureAwait(false);
                history.Track(session.SessionId, job);
                jobs.Add(job);
            }

            return new GenerationResponse(jobs.Select(DescribeJob).ToArray(), [.. unavailable]);
        });

    [McpServerTool(Name = "read_proxy_status", ReadOnly = true)]
    [Description("Reports proxy states and pending jobs for the requested media or for the scene's video sources. Pass jobIds to poll generation, including recent terminal results.")]
    public ToolResult<StatusResponse> ReadProxyStatus(
        [Description("Scene element IDs from read_document_summary.")] string[]? elementIds = null,
        [Description("Absolute paths to original video files. Includes all cached and pending deinterlace variants.")] string[]? mediaPaths = null,
        [Description("Filter proxy entries by half, quarter, or eighth.")] string? preset = null,
        [Description("Job IDs returned by generate_proxies. With only jobIds, returns job status.")] Guid[]? jobIds = null)
        => Execute(() =>
        {
            ProxyPreset? quality = ReadPreset(preset);
            IEditingSession session = sessions.RequireSession();
            var (store, queue) = ReadServices(session);
            SourceSelection selection = ReadProxySelection(session, elementIds, mediaPaths, quality, includeAll: jobIds is not { Length: > 0 });
            var jobs = queue.Pending().Where(job => selection.Matches(job.Source, job.Preset, job.Deinterlace))
                .ToDictionary(job => job.JobId);
            var entries = store.Enumerate().Where(entry => selection.Matches(entry.Source, entry.Preset, entry.Deinterlace))
                .ToLookup(entry => (entry.Source.AbsolutePath, entry.Deinterlace, entry.Preset));
            var sources = selection.ElementSources.Values.ToList();
            if (selection.ExplicitPaths.Count > 0)
            {
                var variantsByPath = entries.Select(group => (group.Key.AbsolutePath, group.Key.Deinterlace))
                    .Concat(jobs.Values.Select(job => (job.Source.AbsolutePath, job.Deinterlace)))
                    .ToLookup(variant => variant.AbsolutePath, variant => variant.Deinterlace, StringComparer.Ordinal);
                foreach (Source path in selection.ExplicitPaths.Values)
                {
                    // A path with no matching proxies or jobs still has a missing status.
                    foreach (VideoDeinterlaceOptions variant in variantsByPath[path.Key].Distinct().DefaultIfEmpty())
                        sources.Add(path with { Deinterlace = variant });
                }
            }

            var unknown = new List<Guid>();
            foreach (Guid id in (jobIds ?? []).Distinct())
            {
                if (history.Find(session.SessionId, id) is { } job) jobs[id] = job;
                else if (!jobs.ContainsKey(id)) unknown.Add(id);
            }

            ProxyPreset[] presets = quality is { } selected ? [selected] : [.. ProxyPresetDefinitions.All.Keys];
            ProxyInfo[] proxies = [.. from source in sources.DistinctBy(source => (source.Key, source.Deinterlace))
                from variant in presets
                select DescribeProxy(source, variant, entries[(source.Key, source.Deinterlace, variant)])];
            return new StatusResponse(proxies, jobs.Values.Select(DescribeJob).ToArray(), unknown);
        });

    [McpServerTool(Name = "remove_proxies")]
    [Description("Cancels matching generation jobs and removes proxies for the selected media from the running editor's cache. Original media and the edit history are preserved.")]
    public ValueTask<ToolResult<RemoveResponse>> RemoveProxies(
        [Description("Scene element IDs from read_document_summary. Supply elementIds, mediaPaths, or both.")] string[]? elementIds = null,
        [Description("Absolute paths to original videos, including offline sources.")] string[]? mediaPaths = null,
        [Description("Remove half, quarter, or eighth. Omit to remove all presets for the selected sources.")] string? preset = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            RequireSelection(elementIds, mediaPaths);
            ProxyPreset? quality = ReadPreset(preset);
            IEditingSession session = sessions.RequireSession();
            var (store, queue) = ReadServices(session);
            SourceSelection selection = ReadProxySelection(session, elementIds, mediaPaths, quality, includeAll: false);
            var pending = queue.Pending().Where(job => selection.Matches(job.Source, job.Preset, job.Deinterlace)).ToArray();
            await CancelJobsAsync(queue, pending, cancellationToken).ConfigureAwait(false);

            var removed = new List<ProxyInfo>();
            var failed = new List<ProxyInfo>();
            foreach (ProxyEntry entry in store.Enumerate().Where(entry => selection.Matches(entry.Source, entry.Preset, entry.Deinterlace)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Source source = selection.ElementSources.GetValueOrDefault((entry.Source.AbsolutePath, entry.Deinterlace))
                    ?? (selection.ExplicitPaths[entry.Source.AbsolutePath] with { Deinterlace = entry.Deinterlace });
                ProxyInfo info = DescribeProxy(source, entry.Preset, [entry]);
                if (store.Delete(entry.Source, entry.Preset, entry.Deinterlace)) removed.Add(info);
                else if (store.TryGet(entry.Source, entry.Preset, entry.Deinterlace) is not null) failed.Add(info);
            }

            return new RemoveResponse(removed, failed,
                pending.Where(job => job.Status == ProxyJobStatus.Canceled).Select(job => job.JobId).ToArray());
        });

    private (IProxyStore Store, IProxyJobQueue Queue) ReadServices(IEditingSession session)
        => session.ReadOnSession(() =>
        {
            var services = editor.AppServices.GetService<ProxyMediaServices>()
                ?? throw new ReconcileException(new ToolError(ErrorCode.RenderingUnavailable, "Proxy services are unavailable in this editor."));
            // Capture one store/queue pair. The queue must still report terminal events if a settings change replaces it.
            return ((IProxyStore)services.Store, (IProxyJobQueue)services.Queue);
        });

    private static SourceSelection ReadProxySelection(IEditingSession session, string[]? elementIds, string[]? mediaPaths,
        ProxyPreset? preset, bool includeAll)
    {
        var paths = (mediaPaths ?? [])
            .Select(path => ResolveSource((MissingMediaTools.ReadAbsolutePath(path, nameof(mediaPaths)), default)))
            .DistinctBy(source => source.Key).ToDictionary(source => source.Key, StringComparer.Ordinal);
        var elements = ReadSources(session, elementIds, mediaPaths: null, includeAll: includeAll && paths.Count == 0)
            .ToDictionary(source => (source.Key, source.Deinterlace));
        return new SourceSelection(elements, paths, preset);
    }

    private static Source[] ReadSources(IEditingSession session, string[]? elementIds, string[]? mediaPaths, bool includeAll)
    {
        string[] paths = (mediaPaths ?? []).Select(path => MissingMediaTools.ReadAbsolutePath(path, nameof(mediaPaths))).ToArray();
        Guid[] ids = (elementIds ?? []).Select(value => Guid.TryParse(value, out Guid id) && id != Guid.Empty
            ? id : throw InvalidInput("Supply element IDs from read_document_summary.", nameof(elementIds))).Distinct().ToArray();
        if (ids.Length == 0 && paths.Length == 0 && !includeAll) return [];

        var (selected, sceneSources) = session.ReadOnSession(() =>
        {
            Scene scene = RequireSceneRoot(session);
            var byId = scene.Children.ToLookup(element => element.Id.Value);
            Element[] elements = ids.Select(id => byId[id].Take(2).ToArray() switch
            {
                [Element element] => element,
                [] => throw new ReconcileException(new ToolError(ErrorCode.StaleHandle, $"Element '{id}' was not found in the target scene.", id.ToString())),
                _ => throw InvalidInput($"More than one element has ID '{id}' in the target scene.", id.ToString())
            }).ToArray();

            return (EnumerateSources(elements), paths.Length > 0 || (includeAll && ids.Length == 0)
                ? EnumerateSources(scene.Children) : []);
        });

        var targets = selected.Select(ResolveSource).ToList();
        if (paths.Length > 0)
        {
            var byPath = sceneSources.Select(ResolveSource).ToLookup(source => source.Key, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                Source direct = ResolveSource((path, VideoDeinterlaceOptions.Default));
                var matches = byPath[direct.Key];
                if (matches.Any()) targets.AddRange(matches);
                else targets.Add(direct);
            }
        }
        else if (includeAll && ids.Length == 0)
        {
            targets.AddRange(sceneSources.Select(ResolveSource));
        }

        return targets.DistinctBy(source => (source.Key, source.Deinterlace)).ToArray();
    }

    private static (string, VideoDeinterlaceOptions)[] EnumerateSources(IEnumerable<Element> elements)
    {
        HashSet<(Scene, Beutl.Composition.CompositionTarget?)> visited = [];
        return [.. from element in elements
                   from use in ProxySourceEnumerator.EnumerateVideoSourceUses(element, visited)
                   where use.Source.HasUri && use.Source.Uri.IsFile
                   select (use.Source.Uri.LocalPath, use.Deinterlace.Normalize())];
    }

    private static Source ResolveSource((string Path, VideoDeinterlaceOptions Deinterlace) use)
        => ProxyFingerprint.TryFromFile(use.Path, out ProxyFingerprint fingerprint)
            ? new Source(use.Path, fingerprint.AbsolutePath, fingerprint, use.Deinterlace)
            : new Source(use.Path, ProxyFingerprint.NormalizeAbsolutePath(use.Path), null, use.Deinterlace);

    private static ProxyInfo DescribeProxy(Source source, ProxyPreset preset, IEnumerable<ProxyEntry> entries)
    {
        ProxyEntry? entry = entries.FirstOrDefault(entry => entry.Source == source.Fingerprint)
            ?? entries.OrderByDescending(entry => entry.GeneratedAtUtc).FirstOrDefault();
        string state = entry is null ? "missing" : Lower(entry.State);
        if (entry is { State: ProxyState.Ready } && source.Fingerprint is { } fingerprint && entry.Source != fingerprint)
            state = "stale";
        return new ProxyInfo(source.Path, Lower(preset), source.Deinterlace, state, source.Fingerprint is not null, entry?.FailureReason);
    }

    private static JobInfo DescribeJob(ProxyJob job)
        => new(job.JobId, job.Source.SourcePath, Lower(job.Preset), job.Deinterlace, Lower(job.Status),
            job.Status == ProxyJobStatus.Succeeded ? 1 : job.LatestProgress?.FractionComplete,
            job.Error?.Message ?? job.StatusMessage);

    private static async Task CancelJobsAsync(IProxyJobQueue queue, ProxyJob[] jobs, CancellationToken cancellationToken)
    {
        if (jobs.Length == 0) return;

        // A running encode may already be publishing. Let its cancellation/cleanup settle before deleting entries.
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnJobChanged(object? sender, ProxyJobChangedEventArgs e)
        {
            if (jobs.All(IsTerminal)) stopped.TrySetResult();
        }

        queue.JobChanged += OnJobChanged;
        try
        {
            foreach (ProxyJob job in jobs) queue.Cancel(job.JobId);
            if (!jobs.All(IsTerminal)) await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            queue.JobChanged -= OnJobChanged;
        }
    }

    private static bool IsTerminal(ProxyJob job) => job.Status is not (ProxyJobStatus.Queued or ProxyJobStatus.Running);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static ProxyPreset? ReadPreset(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "half" => ProxyPreset.Half,
        "quarter" => ProxyPreset.Quarter,
        "eighth" => ProxyPreset.Eighth,
        _ => throw InvalidInput("Choose half, quarter, or eighth.", "preset")
    };

    private static void RequireSelection(string[]? elementIds, string[]? mediaPaths)
    {
        if (elementIds is not { Length: > 0 } && mediaPaths is not { Length: > 0 })
            throw InvalidInput("Supply at least one element ID or media path.", "elementIds");
    }

    private static ReconcileException InvalidInput(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));

    internal sealed class JobHistory
    {
        private const int MaximumRetainedCompletedJobs = 128;
        private readonly Lock _sync = new();
        private readonly Dictionary<(string, Guid), (ProxyJob Job, long Order)> _jobs = [];
        private long _order;

        internal void Track(string sceneId, ProxyJob job)
        {
            lock (_sync)
            {
                _jobs[(sceneId, job.JobId)] = (job, ++_order);
                foreach (var old in _jobs.Where(item => IsTerminal(item.Value.Job))
                             .OrderByDescending(item => item.Value.Order).Skip(MaximumRetainedCompletedJobs).ToArray())
                    _jobs.Remove(old.Key);
            }
        }

        internal ProxyJob? Find(string sceneId, Guid jobId)
        {
            lock (_sync) return _jobs.GetValueOrDefault((sceneId, jobId)).Job;
        }
    }
}
