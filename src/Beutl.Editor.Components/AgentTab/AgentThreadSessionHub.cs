using Beutl.Agents;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab;

internal sealed class AgentThreadSessionHub(string beutlHome, IAgentNotificationSoundPlayer? soundPlayer = null) : IAsyncDisposable
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
            _projects.Add(path, project = new(path, _drafts, _lifetime.Token, soundPlayer));
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

internal enum AgentThreadNotificationKind { Approval, Question, Completed, Failed }

internal sealed class AgentProjectThreadSessions(string path, AgentDraftStore drafts, CancellationToken lifetime,
    IAgentNotificationSoundPlayer? soundPlayer)
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentProjectThreadSessions>();
    private readonly Dictionary<Guid, AgentThreadController> _controllers = [];
    private readonly Dictionary<Guid, int> _viewers = [];
    private Task _draftWrites = Task.CompletedTask;
    private Task _metadataWork = Task.CompletedTask;

    public string ProjectPath => path;
    public CancellationToken Lifetime => lifetime;
    public event Action<Guid>? StateChanged;
    public event Action<Guid, AgentThreadMetadata?>? MetadataChanged;
    public event Action<Guid, DateTimeOffset?, DateTimeOffset?>? ActivityChanged;
    public event Action<Guid>? ActivityNotified;
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

    public void PublishMetadata(AgentThreadMetadata thread)
    {
        Find(thread.Id)?.UpdateTitle(thread.Title);
        MetadataChanged?.Invoke(thread.Id, thread);
    }

    public void AttachViewer(Guid threadId) => _viewers[threadId] = _viewers.GetValueOrDefault(threadId) + 1;

    public void DetachViewer(Guid threadId)
    {
        if (_viewers.GetValueOrDefault(threadId) is > 1 and var count)
            _viewers[threadId] = count - 1;
        else
            _viewers.Remove(threadId);
    }

    public void NotifyActivity(IAgentWorkspaceHost host, IEditorContext editor, Guid threadId, string title,
        DateTimeOffset timestamp, AgentThreadNotificationKind kind)
    {
        bool viewed = _viewers.ContainsKey(threadId);
        ActivityChanged?.Invoke(threadId, timestamp, viewed ? timestamp : null);
        ActivityNotified?.Invoke(threadId);
        if (viewed)
        {
            _ = MarkReadAsync(host.Runtime, threadId, lifetime, timestamp);
            return;
        }

        if (!host.EnableNotifications || lifetime.IsCancellationRequested)
            return;

        try
        {
            if (editor.GetService<INotificationService>() is { } notifications)
            {
                switch (kind)
                {
                    case AgentThreadNotificationKind.Approval:
                        notifications.ShowWarning(AgentStrings.NotificationApproval, title);
                        break;
                    case AgentThreadNotificationKind.Question:
                        notifications.ShowWarning(AgentStrings.NotificationQuestion, title);
                        break;
                    case AgentThreadNotificationKind.Completed:
                        notifications.ShowInformation(AgentStrings.NotificationCompleted, title);
                        break;
                    case AgentThreadNotificationKind.Failed:
                        notifications.ShowError(AgentStrings.TurnFailed, title);
                        break;
                }
            }
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogDebug(ex, "Could not show an agent notification.");
        }

        if (host.EnableNotificationSound && soundPlayer is not null)
            _ = PlaySoundAsync(soundPlayer);
    }

    private async Task PlaySoundAsync(IAgentNotificationSoundPlayer player)
    {
        try
        {
            await player.PlayAsync(lifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogDebug(ex, "Could not play an agent notification sound.");
        }
    }

    public Task MarkReadAsync(
        AgentRuntimeManager runtime, Guid threadId, CancellationToken cancellationToken, DateTimeOffset? observedThrough = null)
    {
        Task work = MarkReadCoreAsync(runtime, threadId, cancellationToken, observedThrough);
        TrackMetadataWork(work);
        return work;
    }

    public void TrackMetadataWork(Task work)
        => _metadataWork = _metadataWork.IsCompletedSuccessfully ? work : Task.WhenAll(_metadataWork, work);

    private async Task MarkReadCoreAsync(
        AgentRuntimeManager runtime, Guid threadId, CancellationToken cancellationToken, DateTimeOffset? observedThrough)
    {
        try
        {
            var thread = await runtime.MarkReadAsync(path, threadId, cancellationToken, observedThrough);
            ActivityChanged?.Invoke(threadId, thread.LastActivityAt, thread.LastReadAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (KeyNotFoundException) when (!_controllers.ContainsKey(threadId))
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not mark agent thread {ThreadId} as read.", threadId);
        }
    }

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
        await _metadataWork;
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
