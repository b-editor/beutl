using Avalonia.Threading;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.Editor.Components.MissingMediaTab;
using Beutl.Editor.Components.MissingMediaTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.AgentHost;

public sealed class EditViewModelLiveBinding(EditViewModel editViewModel, AgentEditFollower? follower = null)
    : ILiveSessionBinding, ILiveElementBinding, ILiveMediaRepairBinding
{
    private static readonly ILogger s_logger = Log.CreateLogger<EditViewModelLiveBinding>();

    public Scene? ActiveScene => editViewModel.Scene;

    public HistoryManager? ActiveHistory => editViewModel.HistoryManager;

    // Host-controlled transitions disable the editor before closing or replacing its scene. An
    // accepted mutation in that window would only reach stale in-memory state, so the session is
    // unavailable for exactly as long as the editor is disabled.
    public bool IsAlive
    {
        get
        {
            if (editViewModel.Scene is null)
            {
                return false;
            }

            try
            {
                return editViewModel.IsEnabled.Value
                       && !editViewModel.IsDisposingOrDisposed;
            }
            catch (ObjectDisposedException)
            {
                // Disposal tears down reactive properties before clearing Scene. Treat that short
                // interval as unavailable instead of leaking the implementation exception.
                return false;
            }
        }
    }

    IReadOnlyList<Element> ILiveElementBinding.GetSelectedElements()
        => editViewModel.GetRequiredService<IEditorSelection>().GetTargetElements(editViewModel.Scene);

    async ValueTask<ElementAddResult> ILiveElementBinding.AddElementsAsync(
        Scene scene, IReadOnlyList<ElementDescription> descriptions,
        Func<Scene, Action?> captureValidation, CancellationToken cancellationToken)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (!IsAlive || !ReferenceEquals(scene, ActiveScene)) throw new SessionUnavailableException();
            var adder = (ElementAdderImpl)editViewModel.GetRequiredService<IElementAdder>();
            return await adder.AddAsync(descriptions, cancellationToken,
                (commit, token) => ExecuteHistoryMutationAsync(
                    _ => IsAlive,
                    history =>
                    {
                        if (!IsAlive) throw new SessionUnavailableException();
                        if (history.IsEditing) throw new UserEditingException();
                        return commit();
                    },
                    token, LiveEditingSession.EditSessionWait),
                captureValidation);
        });
    }

    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvokeAndFollow(action);
        }
        else
        {
            Dispatcher.UIThread.Invoke(() => InvokeAndFollow(action));
        }
    }

    public void InvokeEdit(Action action, TimeSpan sessionWait, CancellationToken cancellationToken)
    {
        var turn = AgentTurnHistoryTracker.RequestTurn;
        void Edit()
        {
            if (turn is null)
                InvokeAndFollow(action);
            else
                turn.TrackEdit(editViewModel.HistoryManager, () => InvokeAndFollow(action));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            // The UI thread cannot wait for the user's gesture, which needs that thread to end.
            if (editViewModel.HistoryManager.IsEditing)
                throw new UserEditingException();

            Edit();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(
                () => RunAfterEditSessionAsync(Edit, sessionWait, cancellationToken))
            .GetAwaiter()
            .GetResult();
    }

    // Starts the deadline on the UI thread, so time spent waiting for that thread is not counted.
    private async Task RunAfterEditSessionAsync(Action edit, TimeSpan sessionWait, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(sessionWait);
        try
        {
            await editViewModel.HistoryManager.RunAfterEditAsync(edit, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new UserEditingException();
        }
    }

    // Every agent mutation reaches the editor through this binding, so the history stacks before
    // and after the call show which transaction it committed, undid or redid.
    private TResult InvokeAndFollow<TResult>(Func<TResult> operation)
    {
        AgentEditFollower.HistoryMark? mark = follower?.Capture(editViewModel);
        TResult result = operation();
        if (mark is { } before)
            follower!.FollowHistoryChange(editViewModel, before);
        return result;
    }

    private void InvokeAndFollow(Action action)
        => InvokeAndFollow(() =>
        {
            action();
            return 0;
        });

    public async ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken = default,
        TimeSpan? sessionWait = null)
    {
        ArgumentNullException.ThrowIfNull(shouldPause);
        ArgumentNullException.ThrowIfNull(operation);

        async ValueTask<TResult> ExecuteCoreAsync()
        {
            if (sessionWait is { } wait)
                await RunAfterEditSessionAsync(static () => { }, wait, cancellationToken);

            HistoryManager history = editViewModel.HistoryManager;
            return await editViewModel.ExecuteGuardedHistoryMutationAsync(
                () => shouldPause(history),
                () => InvokeAndFollow(() => operation(history)),
                cancellationToken);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ExecuteCoreAsync();
        }

        return await Dispatcher.UIThread.InvokeAsync(async () => await ExecuteCoreAsync());
    }

    MissingMediaService ILiveMediaRepairBinding.MissingMediaDetector => ((IMediaRepairHost)editViewModel).MissingMediaDetector;

    IReadOnlyList<MediaReference> ILiveMediaRepairBinding.MediaReferencesForRepair
        => ((IMediaRepairHost)editViewModel).GetMediaReferencesForRepair();

    async ValueTask<(MediaRepairSelection[] Applied, IReadOnlyList<MissingMedia> Remaining)> ILiveMediaRepairBinding.ApplyMediaRepairsAsync(
        Scene scene, IReadOnlyList<MediaRepairSelection> selections, TimeSpan editWait, CancellationToken cancellationToken)
    {
        async Task<(MediaRepairSelection[], IReadOnlyList<MissingMedia>)> ApplyAsync()
        {
            CheckScene();
            IMediaRepairHost host = editViewModel;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, host.RepairCancellationToken);
            CancellationToken token = cancellation.Token;
            await RunAfterEditSessionAsync(static () => { }, editWait, token);
            using var write = await editViewModel.Workspace.BeginProjectFileWriteAsync(token);

            MediaRepairSelection[] applied;
            IReadOnlyList<MissingMedia> remaining;
            // Suspend inside the playback guard, in the same turn that drains the player and pending edits.
            using (await editViewModel.ExecuteGuardedHistoryMutationAsync(
                () => { CheckScene(); return true; },
                () =>
                {
                    CheckScene();
                    token.ThrowIfCancellationRequested();
                    if (editViewModel.HistoryManager.IsEditing) throw new UserEditingException();
                    return host.SuspendEditor();
                }, token))
            {
                var detector = host.MissingMediaDetector;
                var validated = new List<MissingMediaRepair.Item>();
                foreach (var selection in selections)
                    validated.Add(await MissingMediaRepair.ValidateAsync(detector, selection, token));

                var repair = MissingMediaRepair.Create(scene, editViewModel.HistoryManager,
                    editViewModel.GetRequiredService<FontManager>(), host.ReloadBundledFonts,
                    host.GetMediaReferencesForRepair(), validated) ?? throw MissingMediaTools.SceneChanged();
                applied = repair.Applied;
                if (applied.Length > 0)
                {
                    bool hadRepairs = host.HasMediaRepairs;
                    try
                    {
                        using (repair)
                        {
                            await repair.ApplyAsync(token);
                            token.ThrowIfCancellationRequested();
                            host.HasMediaRepairs = true;
                            if (!await host.SaveRepairsAsync()) throw new IOException(MessageStrings.UnableToSaveFile);
                            repair.Commit();
                        }
                    }
                    catch
                    {
                        host.HasMediaRepairs = hadRepairs;
                        throw;
                    }
                    finally
                    {
                        if (!editViewModel.IsDisposingOrDisposed) host.ClearRenderCaches();
                    }
                }

                remaining = await detector.FindMissingAsync(scene, host.GetMediaReferencesForRepair(), host.RepairCancellationToken,
                    revalidateExisting: true);
                if (!editViewModel.IsDisposingOrDisposed)
                {
                    editViewModel.NotifyMissingMedia();
                    if (editViewModel.FindToolTab<MissingMediaViewModel>() is { IsBusy.Value: false } tab)
                        tab.RefreshRows(remaining);
                }
            }

            if (applied.Length > 0 && editViewModel.GetService(typeof(IProjectVersionControlCoordinator)) is IProjectVersionControlSession projectSession)
            {
                try
                {
                    await projectSession.NotifySavedAsync(write, host.RepairCancellationToken);
                    host.ScheduleMediaFingerprints(finishSaveSnapshot: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    s_logger.LogWarning(ex, "Could not snapshot the saved media repair.");
                }
            }

            return (applied, remaining);
        }

        void CheckScene()
        {
            if (!IsAlive || !ReferenceEquals(scene, ActiveScene)) throw new SessionUnavailableException();
        }

        return Dispatcher.UIThread.CheckAccess() ? await ApplyAsync() : await Dispatcher.UIThread.InvokeAsync(ApplyAsync);
    }
}
