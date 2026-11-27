using Beutl.Agents;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab;

internal sealed class AgentThreadSessionHub(string beutlHome) : IAsyncDisposable
{
    private readonly AgentDraftStore _drafts = new(beutlHome);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, AgentProjectThreadSessions> _projects =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public AgentProjectThreadSessions ForProject(string projectPath)
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        if (!_projects.TryGetValue(path, out var project))
            _projects.Add(path, project = new(path, _drafts, _lifetime.Token));
        return project;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;

        await _lifetime.CancelAsync();
        await Task.WhenAll(_projects.Values.Select(project => project.ShutdownAsync()));
        _projects.Clear();
        _lifetime.Dispose();
    }
}

internal sealed class AgentProjectThreadSessions(string path, AgentDraftStore drafts, CancellationToken lifetime)
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentProjectThreadSessions>();
    private readonly Dictionary<Guid, AgentThreadController> _controllers = [];
    private Task _draftWrites = Task.CompletedTask;

    public string ProjectPath => path;
    public CancellationToken Lifetime => lifetime;
    public event Action<Guid>? StateChanged;
    public event Action<Guid, AgentThreadMetadata?>? MetadataChanged;
    public event Action? FlushingDrafts;

    public AgentThreadController GetOrCreate(IAgentWorkspaceHost host, Guid threadId)
    {
        lifetime.ThrowIfCancellationRequested();
        if (!_controllers.TryGetValue(threadId, out var controller))
            _controllers.Add(threadId, controller = new(host, this, threadId));
        return controller;
    }

    public AgentThreadController? Find(Guid threadId) => _controllers.GetValueOrDefault(threadId);

    public void PublishState(Guid threadId) => StateChanged?.Invoke(threadId);

    public void PublishMetadata(AgentThreadMetadata thread) => MetadataChanged?.Invoke(thread.Id, thread);

    public async Task RemoveAsync(Guid threadId)
    {
        if (_controllers.Remove(threadId, out var controller))
            await controller.ShutdownAsync();
        MetadataChanged?.Invoke(threadId, null);
        await QueueDraftWrite(() => drafts.DeleteAsync(path, threadId));
    }

    public async Task<AgentDraftEntry> LoadDraftAsync(Guid? threadId, CancellationToken cancellationToken)
    {
        await _draftWrites.WaitAsync(cancellationToken);
        return await drafts.LoadAsync(path, threadId, cancellationToken);
    }

    public Task SaveDraftAsync(Guid? threadId, AgentDraft? draft) =>
        QueueDraftWrite(() => drafts.SaveAsync(path, threadId, draft));

    public void RecordPrompt(Guid threadId, string prompt) =>
        _ = QueueDraftWrite(() => drafts.RecordPromptAsync(path, threadId, prompt));

    public void SeedHistory(Guid threadId, IReadOnlyList<string> history) =>
        _ = QueueDraftWrite(() => drafts.SeedHistoryAsync(path, threadId, history));

    public async Task ShutdownAsync()
    {
        FlushingDrafts?.Invoke();
        await Task.WhenAll(_controllers.Values.Select(controller => controller.ShutdownAsync()));
        await _draftWrites;
        _controllers.Clear();
    }

    private Task QueueDraftWrite(Func<Task> write)
    {
        var pending = WriteAfterAsync(_draftWrites, write);
        _draftWrites = ObserveWriteAsync(pending);
        return pending;
    }

    private static async Task WriteAfterAsync(Task previous, Func<Task> write)
    {
        await previous.ConfigureAwait(false);
        await write().ConfigureAwait(false);
    }

    private static async Task ObserveWriteAsync(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not save agent drafts or prompt history.");
        }
    }
}
