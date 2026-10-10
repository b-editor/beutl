using Avalonia.Threading;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void PublishNotification(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish a version-control notification.");
        }
    }

    private Task<bool> ShowSwitchBranchConfirmationAsync(
        string branchName,
        CancellationToken cancellationToken)
    {
        return _prompts.ShowConfirmationAsync(
            Strings.VersionControl_SwitchBranch,
            CreateSwitchBranchConfirmation(
                branchName,
                CurrentService?.Repository?.IsNestedInForeignRepo == true),
            cancellationToken);
    }

    // A branch switch is repository-wide by design, so a project sharing someone else's repository
    // has to be told that the decision reaches past its own directory before it is taken.
    internal static string CreateSwitchBranchConfirmation(
        string branchName,
        bool isNestedInForeignRepo)
    {
        string confirmation = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_SwitchBranchConfirmation,
            branchName);
        return isNestedInForeignRepo
            ? $"{confirmation}\n\n{Strings.VersionControl_SwitchBranchEnclosingRepositoryNotice}"
            : confirmation;
    }

    private async Task<bool> ShowAdoptExistingRepositoryConfirmationAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        CancellationToken token = cancellation.Token;
        var request = new RepositoryAdoptionRequest(repository);
        using CancellationTokenRegistration registration = token.Register(() => request.Cancel(token));
        VersionControlTabViewModel? presentedTab = null;
        // A selection change is still being applied when it notifies, so the tab is shown once it has.
        using IDisposable selection = _editorService.SelectedTabItem.Subscribe(
            _ => _dispatcher.Post(ShowConfirmationTab, DispatcherPriority.Normal));
        try
        {
            // Asked from a later dispatcher job, so the editors of a project that is just opening exist.
            await _dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                if (_close is not null)
                {
                    request.Respond(false);
                    return;
                }

                RepositoryAdoptionRequest? previous = _pendingRepositoryAdoption;
                _pendingRepositoryAdoption = request;
                previous?.Respond(false);
                RepositoryAdoptionChanged?.Invoke(this, EventArgs.Empty);
                ShowConfirmationTab();
            }, DispatcherPriority.Normal, token);
            return await request.Completion;
        }
        finally
        {
            if (presentedTab is not null)
                presentedTab.Disposed -= OnPresentedTabDisposed;
            request.Respond(false);
            if (ReferenceEquals(_pendingRepositoryAdoption, request))
            {
                _pendingRepositoryAdoption = null;
                RepositoryAdoptionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        void ShowConfirmationTab()
        {
            if (request.Completion.IsCompleted || token.IsCancellationRequested
                || !ReferenceEquals(PendingRepositoryAdoption, request))
            {
                return;
            }

            if (_editorService.SelectedTabItem.Value is not { } selected
                || !_editorService.TabItems.Contains(selected)
                || selected.Context.Value is not EditViewModel editor
                || _projectService.CurrentProject.Value is not { } project
                || !project.Items.Contains(editor.Scene))
            {
                request.Respond(false);
                return;
            }

            VersionControlTabViewModel? existing = editor.FindToolTab<VersionControlTabViewModel>();
            if (existing is not null && ReferenceEquals(existing, presentedTab))
                return;

            var tab = existing ?? new VersionControlTabViewModel(VersionControlTabExtension.Instance, editor);
            if (!editor.OpenToolTab(tab))
            {
                request.Respond(false);
                if (existing is null)
                    tab.Dispose();
                return;
            }

            if (presentedTab is not null)
                presentedTab.Disposed -= OnPresentedTabDisposed;
            presentedTab = tab;
            presentedTab.Disposed += OnPresentedTabDisposed;
        }

        void OnPresentedTabDisposed(object? sender, EventArgs e) => request.Respond(false);
    }

    // The backend asks from its own Git continuation, but the identity prompt is a flyout that has to be
    // created on the UI thread, like the one a manual commit shows.
    private Task<GitIdentity?> RequestIdentityForSnapshotAsync(CancellationToken cancellationToken)
    {
        return _dispatcher.CheckAccess()
            ? RequestIdentityAsync(cancellationToken)
            : _dispatcher.InvokeAsync(
                () => RequestIdentityAsync(cancellationToken),
                DispatcherPriority.Normal);
    }
}
