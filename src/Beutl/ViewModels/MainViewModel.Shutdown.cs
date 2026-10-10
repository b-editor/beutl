using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Helpers;
using Beutl.Services;
using DynamicData;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MainViewModel
{
    public override void Dispose()
    {
        try
        {
            BeginDisposeOrThrow();
        }
        catch (ProjectCloseAbortedException)
        {
        }
    }

    private void BeginDisposeOrThrow()
    {
        Task<bool>? sharedClose;
        lock (_disposeGate)
        {
            // Disposal is only published after the project close was accepted, and
            // DisposeCoreAsync closes the project again itself, so a repeated request
            // must not pump the UI thread through another synchronous close.
            if (_disposeTask is not null)
                return;

            sharedClose = _closeForShutdownTask;
        }

        if (sharedClose is { IsCompleted: false })
        {
            // A window or shutdown request is already closing the project. Join that
            // attempt instead of opening a second transition that would pump the UI
            // thread behind the project gate and run the close and veto handlers again.
            WaitOnUiThread(sharedClose);
            if (!sharedClose.GetAwaiter().GetResult())
            {
                throw new ProjectCloseAbortedException(
                    "The in-flight project close was refused.");
            }

            return;
        }

        _projectService.CloseProjectOrThrow();
        Task<bool> saveEditors = _editorService.SaveSceneEditorsBeforeCloseAsync(_editorService.TabItems.ToArray());
        WaitOnUiThread(saveEditors);
        if (!saveEditors.GetAwaiter().GetResult())
            throw new ProjectCloseAbortedException(MessageStrings.FileSaveException);

        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }
    }

    private static void WaitOnUiThread(Task task)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            while (!task.IsCompleted)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
        }
        else
        {
            task.GetAwaiter().GetResult();
        }
    }

    internal bool TryDisposeForWindowClose()
    {
        try
        {
            BeginDisposeOrThrow();
            return true;
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }
    }

    internal Task<bool> TryDisposeForWindowCloseAsync()
    {
        lock (_disposeGate)
        {
            // The window closing path and the desktop shutdown request share one close
            // attempt so that overlapping requests neither save twice nor prompt twice.
            // A vetoed or failed attempt must not answer the next request.
            if (_closeForShutdownTask is { IsCompleted: true } finished
                && !(finished.IsCompletedSuccessfully && finished.Result))
            {
                _closeForShutdownTask = null;
            }

            return _closeForShutdownTask ??= CloseProjectAndBeginDisposeAsync();
        }
    }

    internal Task<bool> TryDisposeForUpdateAsync(Func<bool> startInstaller)
    {
        ArgumentNullException.ThrowIfNull(startInstaller);
        TaskCompletionSource<bool> completion;
        lock (_disposeGate)
        {
            // An update must not launch from a second, overlapping close request.
            if (_disposeTask is not null || _closeForShutdownTask is { IsCompleted: false })
                return Task.FromResult(false);

            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeForShutdownTask = completion.Task;
        }

        // Publish the shared close before invoking callbacks that may re-enter the shell.
        _ = CompleteUpdateCloseAsync(startInstaller, completion);
        return completion.Task;
    }

    private async Task CompleteUpdateCloseAsync(Func<bool> startInstaller, TaskCompletionSource<bool> completion)
    {
        try
        {
            completion.TrySetResult(await CloseProjectAndBeginDisposeAsync(startInstaller));
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task<bool> CloseProjectAndBeginDisposeAsync(Func<bool>? beforeDispose = null)
    {
        try
        {
            if (beforeDispose is null)
            {
                await _projectService.CloseProjectAsync();
                if (!await _editorService.SaveSceneEditorsBeforeCloseAsync(_editorService.TabItems.ToArray()))
                    return false;
            }
            else
            {
                await _projectService.CloseProjectForUpdateAsync(async () =>
                    await _editorService.SaveSceneEditorsBeforeCloseAsync(_editorService.TabItems.ToArray())
                    && beforeDispose());
            }
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }

        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }

        return true;
    }

    internal Task WaitForDisposalAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ?? Task.CompletedTask;
        }
    }

    internal Task WaitForShutdownRequestAsync()
    {
        lock (_disposeGate)
        {
            return _shutdownRequestTask ?? Task.CompletedTask;
        }
    }

    private bool IsDisposalComplete()
    {
        lock (_disposeGate)
        {
            return _disposeTask is { IsCompleted: true };
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            PackageInstaller packageInstaller = _beutlClients.GetResource<PackageInstaller>();
            _packageInstallerForShutdown = packageInstaller;
            packageInstaller.BeginShutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin package installer shutdown.");
        }
        try
        {
            // The host uses project/editor services, so join its complete lifecycle before
            // closing either service.
            await _agentHostEndpoint.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the agent host during shutdown.");
        }
        try
        {
            _aiJobCompletionNotifier.Dispose();
            CommandPalette.Dispose();
            TabSwitcher.Dispose();
            Status.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose shell notification services during shutdown.");
        }

        try
        {
            TitleBarBranch.Dispose();
            _versionControlCoordinator.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose version-control shell services during shutdown.");
        }

        await CloseEditorSessionAsync();

        // Recovery source publication markers belong to the editor operations
        // above. Release their cross-process locks only after every tab has
        // canceled and drained its paid-AI work.
        _aiRequestRecoveryContext.Dispose();

        try
        {
            await _aiJobResultHandlers.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drain AI job result handlers during shutdown.");
        }

        try
        {
            await _captionCatalog.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drain caption registrations during shutdown.");
        }

        try
        {
            if (ProxyMediaServices.Current is { } proxyMediaServices)
            {
                await proxyMediaServices.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxy media services failed to dispose during shutdown.");
        }

        try
        {
            BeutlApplication.Current.Items.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear application services during shutdown.");
        }

        await CompleteShutdownAsync();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Volatile.Write(ref _exitObserved, 1);
        if (sender is IControlledApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
            if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownRequested -= OnShutdownRequested;
            }
        }

        // Exit stops the dispatcher as soon as this handler returns, so anything still
        // pending here is lost. The desktop shutdown request is intercepted below to
        // drain the asynchronous close first; this remains the synchronous fallback for
        // forced Shutdown() callers and non-desktop lifetimes.
        CompleteShutdown();
    }

    internal void RegisterExitHandler(IControlledApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.Exit -= OnExit;
        lifetime.Exit += OnExit;

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_desktopLifetime is { } previous && !ReferenceEquals(previous, desktop))
            {
                previous.Exit -= OnExit;
                previous.ShutdownRequested -= OnShutdownRequested;
            }

            _desktopLifetime = desktop;
            desktop.ShutdownRequested -= OnShutdownRequested;
            desktop.ShutdownRequested += OnShutdownRequested;
        }
    }

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Another handler already refused this request; leave the session untouched.
        if (e.Cancel)
            return;

        // The reissued request, or a window that finished draining on its own: let
        // the lifetime raise Exit and stop the dispatcher.
        if (IsDisposalComplete())
            return;

        IClassicDesktopStyleApplicationLifetime? lifetime =
            sender as IClassicDesktopStyleApplicationLifetime ?? _desktopLifetime;
        if (lifetime is null)
            return;

        // Refuse this request while the close and disposal drain with the dispatcher
        // still running, then ask the lifetime to shut down again. Repeated requests
        // join the in-flight drain instead of starting a second close.
        e.Cancel = true;
        lock (_disposeGate)
        {
            _shutdownRequestTask ??= DrainShutdownRequestAsync(lifetime);
        }
    }

    private async Task DrainShutdownRequestAsync(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        // Publish the in-flight request before a synchronous veto can clear it.
        await Task.Yield();
        bool closeAccepted = false;
        try
        {
            closeAccepted = await TryDisposeForWindowCloseAsync();
            if (closeAccepted)
            {
                await WaitForDisposalAsync();
            }
        }
        catch (Exception ex)
        {
            // A failed close leaves the project open for a retry; once the close was
            // accepted a cached cleanup failure cannot keep the process alive.
            _logger.LogError(ex, "Failed to drain the editor session for a shutdown request.");
            await ex.Handle();
        }

        if (!closeAccepted)
        {
            lock (_disposeGate)
            {
                _shutdownRequestTask = null;
            }

            return;
        }

        // A window that drained in parallel may have completed the shutdown already.
        if (Volatile.Read(ref _exitObserved) != 0)
            return;

        lifetime.TryShutdown();
    }

    internal void CompleteShutdown()
    {
        Dispose();

        Task? disposal;
        lock (_disposeGate)
        {
            disposal = _disposeTask;
        }

        if (disposal is null || disposal.IsCompleted)
            return;

        // Exit stops the dispatcher as soon as the handler returns, so a disposal that
        // is still running (a forced Shutdown() during a window close, for example)
        // would lose its editor teardown and package handoff. Pump it to completion.
        try
        {
            WaitOnUiThread(disposal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disposal failed while draining for application exit.");
        }
    }

    private async Task CompleteShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownCompleted, 1) != 0)
            return;

        try
        {
            if (_waitForPackageInstallerIdle is not null)
                await _waitForPackageInstallerIdle(TimeSpan.FromSeconds(5));
            else if (_packageInstallerForShutdown is not null)
                await _packageInstallerForShutdown.WaitUntilIdleAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to wait for package operations during shutdown.");
        }

        try
        {
            _shutdownHandoff(_beutlClients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hand package changes to the shutdown helper.");
        }
        finally
        {
            await DisposeApiClientsAsync();
        }
    }

    private async Task CloseEditorSessionAsync()
    {
        EditorTabItem[] tabs = _editorService.TabItems.ToArray();
        try
        {
            _editorService.SelectedTabItem.Value = null;
            _editorService.TabItems.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpublish editor tabs during shutdown.");
        }

        foreach (EditorTabItem tab in tabs)
        {
            try
            {
                await tab.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose an editor tab during shutdown.");
            }
        }

        try
        {
            await _projectService.CloseProjectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close the active project during shutdown.");
        }
    }

    private void PerformShutdownHandoff(BeutlApiApplication clients)
    {
        PackageChangesQueue queue = clients.GetResource<PackageChangesQueue>();
        PackageIdentity[] installs = queue.GetInstalls().ToArray();
        PackageIdentity[] uninstalls = queue.GetUninstalls().ToArray();

        if (installs.Length == 0 && uninstalls.Length == 0)
            return;

        var startInfo = new ProcessStartInfo() { UseShellExecute = true, };
        DotNetProcess.Configure(startInfo, Path.Combine(AppContext.BaseDirectory, "Beutl.PackageTools.UI"));

        AddPackageArguments(startInfo, "--installs", installs);
        AddPackageArguments(startInfo, "--uninstalls", uninstalls);

        startInfo.ArgumentList.AddRange(["--session-id", Telemetry.Instance._sessionId]);

        if (Debugger.IsAttached)
            startInfo.ArgumentList.Add("--launch-debugger");

        Process.Start(startInfo);
    }

    private static void AddPackageArguments(ProcessStartInfo startInfo, string option, PackageIdentity[] packages)
    {
        if (packages.Length > 0)
        {
            startInfo.ArgumentList.Add(option);
            foreach (PackageIdentity? item in packages)
            {
                startInfo.ArgumentList.Add(item.HasVersion ? $"{item.Id}/{item.Version}" : item.Id);
            }
        }
    }

    private Task DisposeApiClientsAsync()
    {
        lock (_apiClientDisposeGate)
        {
            return _apiClientDisposeTask ??= DisposeApiClientsCoreAsync();
        }
    }

    private async Task DisposeApiClientsCoreAsync()
    {
        try
        {
            await _beutlClients.DisposeAsync();
        }
        finally
        {
            _authHttpClient.Dispose();
        }
    }
}
