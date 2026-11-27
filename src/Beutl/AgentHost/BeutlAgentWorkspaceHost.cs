using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Agents;
using Beutl.AgentToolkit.Installation;
using Beutl.AgentToolkit.Live;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Graphics;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Pages;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Beutl.AgentHost;

internal sealed partial class BeutlAgentWorkspaceHost(
    AgentRuntimeManager runtime, AgentHostEndpoint endpoint, AiAgentConfig config, string beutlHome,
    Func<SettingsDialogViewModel>? createDialog = null, IAgentNotificationSoundPlayer? soundPlayer = null) : IAgentWorkspaceHost
{
    private static readonly ILogger s_logger = Log.CreateLogger<BeutlAgentWorkspaceHost>();
    private readonly AgentAttachmentStore _attachmentStore = new(beutlHome);
    private SettingsDialog? _dialog;
    private SettingsDialogViewModel? _settingsViewModel;
    public AgentRuntimeManager Runtime => runtime;
    public AgentThreadSessionHub ThreadSessions { get; } = new(beutlHome, soundPlayer);
    public IProperty<bool> FollowLiveMcpEdits => config.FollowLiveMcpEdits;
    public string DefaultProviderId => config.DefaultRuntimeProviderId.Value?.Trim() ?? "";
    public string? DefaultModel => string.IsNullOrWhiteSpace(config.DefaultModel.Value) ? null : config.DefaultModel.Value.Trim();
    public string? DefaultReasoningEffort => string.IsNullOrWhiteSpace(config.DefaultReasoningEffort.Value)
        ? null : config.DefaultReasoningEffort.Value.Trim();
    public AgentPermissionMode DefaultPermissionMode => AgentRuntimeConfiguration.ParsePermissionMode(config.DefaultPermissionMode.Value);
    public bool SteerOnSend => config.SteerOnSend.Value;
    public AgentComposerSendKey ComposerSendKey => config.ComposerSendKey.Value;
    public bool EnableNotifications => config.EnableNotifications.Value;
    public bool EnableNotificationSound => config.EnableNotificationSound.Value;

    public event EventHandler<PropertyChangedEventArgs>? RuntimeSettingsChanged
    {
        add => config.RuntimeSettingsChanged += value;
        remove => config.RuntimeSettingsChanged -= value;
    }

    public IReadOnlyList<AgentProviderDescriptor> GetProviders()
    {
        try
        {
            return AgentRuntimeConfiguration.GetProviders(config,
                AgentRuntimeConfiguration.ReadCustomProviders(config.CustomRuntimeProvidersJson.Value));
        }
        catch (JsonException ex)
        {
            s_logger.LogWarning(ex, "Could not read custom agent runtime providers.");
            return AgentRuntimeConfiguration.GetProviders(config, []);
        }
    }

    public AgentTabViewModel? CreateWorkspace(IEditorContext editor)
    {
        if (editor is not EditViewModel { Scene: { } scene })
            return null;
        Uri? uri = scene.FindHierarchicalParent<Project>()?.Uri ?? scene.Uri;
        return uri is { IsFile: true } ? new AgentTabViewModel(this, editor, Path.GetFullPath(uri.LocalPath)) : null;
    }

    public async Task OpenAiAgentSettingsAsync(Window owner)
    {
        if (_dialog != null)
        {
            _settingsViewModel!.GoToAiAgentSettingsPage();
            _dialog.Activate();
            return;
        }
        if (createDialog is null)
            return;

        using var vm = createDialog();
        var dialog = new SettingsDialog { DataContext = vm };
        HostServices.CopyFrom(dialog, owner);
        _settingsViewModel = vm;
        _dialog = dialog;
        try
        {
            vm.GoToAiAgentSettingsPage();
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _dialog = null;
            _settingsViewModel = null;
        }
    }

    public async Task<AgentTurnSummary> RunTurnAsync(
        IEditorContext editor, string projectPath, AgentProviderDescriptor provider, AgentTurnRequest request,
        bool includeSceneContext, IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions,
        AgentTurnCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        var edit = (EditViewModel)editor;
        var prepared = await PrepareRequestAsync(edit, projectPath, request, includeSceneContext, attachments, mentions, cancellationToken);
        IReadOnlyList<McpServerSpec> servers = await GetMcpServersAsync(request.ThreadId,
            message => callbacks.Timeline(new AgentErrorNotice(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow, message,
                AgentDiagnosticKind.McpUnavailable)),
            cancellationToken);
        var turn = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureEditorAvailable(edit);
            return endpoint.TurnHistory.TryBegin(request.ThreadId, request.TurnId, edit.HistoryManager);
        }, DispatcherPriority.Normal, cancellationToken) ?? throw new InvalidOperationException(AgentStrings.ThreadBusy);

        try
        {
            return await runtime.RunTurnAsync(projectPath, provider, prepared, servers, callbacks, cancellationToken);
        }
        finally
        {
            int count = await Dispatcher.UIThread.InvokeAsync(() => endpoint.TurnHistory.Finish(turn));
            if (count > 0)
            {
                var entry = new AgentTurnHistoryRecorded(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow, count, turn.Changes.ToArray());
                try
                {
                    await runtime.RecordTurnHistoryAsync(projectPath, entry);
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Could not save the agent turn's history count.");
                    callbacks.Timeline(new AgentErrorNotice(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                        AgentStrings.HistorySaveFailed));
                }
                callbacks.Timeline(entry);
            }
        }
    }

    public async Task<bool> SteerTurnAsync(
        IEditorContext editor, string projectPath, AgentTurnRequest request, bool includeSceneContext,
        IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRequestAsync((EditViewModel)editor, projectPath, request, includeSceneContext, attachments, mentions, cancellationToken);
        return await runtime.SteerTurnAsync(prepared, cancellationToken);
    }

    private async Task<AgentTurnRequest> PrepareRequestAsync(
        EditViewModel edit, string projectPath, AgentTurnRequest request, bool includeSceneContext,
        IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions,
        CancellationToken cancellationToken)
    {
        var context = await Dispatcher.UIThread.InvokeAsync(
            () => CaptureContext(edit, projectPath, includeSceneContext, attachments, mentions),
            DispatcherPriority.Normal, cancellationToken);
        return request with
        {
            Context = context,
            BundledSkills = ResolveBundledSkills(request.Text)
        };
    }

    public async Task<AgentThreadMetadata> CompactContextAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, AgentTurnCallbacks callbacks, CancellationToken cancellationToken)
    {
        var servers = await GetMcpServersAsync(threadId, null, cancellationToken);
        return await runtime.CompactContextAsync(projectPath, threadId, provider, servers, callbacks, cancellationToken);
    }

    public Task<AgentAttachedFile> SavePastedImageAsync(AvaloniaBitmap image, CancellationToken cancellationToken)
        => _attachmentStore.SavePastedImageAsync(image, cancellationToken);

    public Task<AgentAttachedFile> SavePastedTextAsync(string text, CancellationToken cancellationToken)
        => _attachmentStore.SavePastedTextAsync(text, cancellationToken);

    public async Task<AgentAttachedFile> CaptureFrameAsync(IEditorContext editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEditorAvailable((EditViewModel)editor);
        using var frame = await editor.GetRequiredService<IFullScaleFrameRenderer>().DrawFrameAtFullScale();
        return await _attachmentStore.SaveAsync("frame-", ".png", "image/png", stream =>
        {
            if (!frame.Save(stream, EncodedImageFormat.Png))
                throw new IOException(AgentStrings.FrameCaptureFailed);
        }, cancellationToken);
    }

    public bool CanRevertTurn(IEditorContext editor, Guid turnId)
        => editor is EditViewModel { IsDisposingOrDisposed: false } edit && edit.IsEnabled.Value
            && endpoint.TurnHistory.CanRevert(edit.HistoryManager, turnId);

    public async Task<AgentTurnRevertResult> RevertTurnAsync(IEditorContext editor, Guid turnId, CancellationToken cancellationToken)
    {
        if (editor is not EditViewModel { IsDisposingOrDisposed: false } edit || !edit.IsEnabled.Value)
            return AgentTurnRevertResult.Unavailable;

        return await edit.ExecuteGuardedHistoryMutationAsync(
            () => CanRevertTurn(edit, turnId),
            () => edit.IsEnabled.Value ? endpoint.TurnHistory.Revert(edit.HistoryManager, turnId) : AgentTurnRevertResult.Unavailable,
            cancellationToken);
    }

    private async Task<IReadOnlyList<McpServerSpec>> GetMcpServersAsync(
        Guid threadId, Action<string>? report, CancellationToken cancellationToken)
    {
        string? commandOverride = config.StdioCommand.Value?.Trim();
        var server = string.IsNullOrEmpty(commandOverride) ? AgentToolkitMcpServerLocator.ResolveDefault() : new(commandOverride, []);
        if (server is null)
        {
            report?.Invoke(AgentStrings.McpLauncherMissing);
            return [];
        }

        if (report is not null && !endpoint.IsRunning)
        {
            // Startup is shared with the shell. A slow endpoint should not hold up the agent process.
            try
            {
                await endpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                s_logger.LogWarning(ex, "The live MCP endpoint was not ready when the agent turn began.");
                report(AgentStrings.McpNotReady);
            }
        }

        string argumentsOverride = config.StdioArguments.Value;
        IReadOnlyList<string> arguments = string.IsNullOrWhiteSpace(argumentsOverride)
            ? server.Arguments : AgentToolkitMcpServerCommand.ParseArguments(argumentsOverride);
        // Thread IDs only correlate undo history. The stdio broker handles profile authentication.
        return [new StdioMcpServerSpec("beutl", server.Command, arguments, new Dictionary<string, string>
        {
            ["BEUTL_HOME"] = beutlHome,
            [AgentHostPeerClient.AgentThreadEnvironmentVariable] = threadId.ToString("D")
        })];
    }

    private AgentContextSnapshot CaptureContext(EditViewModel editor, string projectPath, bool includeScene,
        IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions)
    {
        EnsureEditorAvailable(editor);
        Scene scene = editor.Scene;
        Project? project = scene.FindHierarchicalParent<Project>();
        string projectName = (project ?? (CoreObject)scene).Name.Value;
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (!includeScene)
            return new(endpoint.InstanceId, projectPath, projectName, null, null, null, null, null, null, null, [], attachments, mentions);

        var size = scene.FrameSize.Value;
        var clock = editor.GetRequiredService<IEditorClock>();
        var selection = editor.GetRequiredService<IEditorSelection>();
        return new(endpoint.InstanceId, projectPath, projectName, scene.Id.Value.ToString("D"), scene.Name.Value,
            size.Width, size.Height, scene.FrameRate.Value.ToDouble(), scene.Duration.Value.ToSeconds(),
            clock.CurrentTime.Value.ToSeconds(), selection.SelectedElements.Select(element => element.Id.Value.ToString("D")).ToArray(),
            attachments, mentions);
    }

    private static void EnsureEditorAvailable(EditViewModel editor)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (editor.IsDisposingOrDisposed || !editor.IsEnabled.Value)
            throw new InvalidOperationException(AgentStrings.EditorUnavailable);
    }
}
