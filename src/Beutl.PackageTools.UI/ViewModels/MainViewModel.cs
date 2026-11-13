using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reactive.Linq;

using Beutl.Helpers;
using Beutl.Logging;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.Services;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class MainViewModel : IAsyncDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly ChangesModel _model;
    private readonly BeutlApiApplication _app;
    private readonly HttpClient _httpClient;
    private readonly AsyncOperationLifetime _operationLifetime;
    private readonly Task _initializationTask;

    private readonly List<ActionViewModel> _viewModels = [];

    private CleanViewModel? _cleanViewModel;

    private readonly Process[] _beutlProcesses;
    private readonly Process[] _pkgProcesses;
    private readonly bool _startsAutomatically;
    private readonly Action _startBeutl;
    private volatile bool _disposeRequested;
    private volatile bool _initializationFailed;
    private volatile bool _startBeutlWhenClosed;

    public MainViewModel()
        : this(new HttpClient())
    {
    }

    // This standalone package-tools process owns its own extension provider; it never
    // loads editor extensions, so a fresh instance is sufficient for the API resource graph.
    private MainViewModel(HttpClient httpClient)
        : this(
            httpClient,
            new BeutlApiApplication(httpClient, new ExtensionProvider()),
            new ChangesModel(),
            [
                .. Process.GetProcessesByName("Beutl"),
                .. Process.GetProcessesByName("beutl")
            ],
            [
                .. Process.GetProcessesByName("Beutl.PackageTools"),
                .. Process.GetProcessesByName("Beutl.PackageTools.UI"),
                .. Process.GetProcessesByName("beutl-pkg"),
            ],
            startsAutomatically: ParseArgs().Restart)
    {
    }

    // startsAutomatically is true when Beutl is restarting (see StartsAutomatically). startBeutl starts Beutl
    // when the user asks for it at the end; by default the Beutl beside these tools.
    internal MainViewModel(
        HttpClient httpClient,
        BeutlApiApplication app,
        ChangesModel model,
        Process[] beutlProcesses,
        Process[] pkgProcesses,
        Func<CancellationToken, Task>? initialize = null,
        Action? cancelPendingRequests = null,
        Func<ValueTask>? disposeResources = null,
        bool startsAutomatically = false,
        Action? startBeutl = null)
    {
        _httpClient = httpClient;
        _app = app;
        _model = model;
        _beutlProcesses = beutlProcesses;
        _pkgProcesses = pkgProcesses;
        _startsAutomatically = startsAutomatically;
        _startBeutl = startBeutl ?? StartBeutl;

        Func<ValueTask> dispose = disposeResources ?? DisposeOwnedResources;
        _operationLifetime = new AsyncOperationLifetime(
            cancelPendingRequests ?? _httpClient.CancelPendingRequests,
            () => DisposeThenStartBeutlAsync(dispose));
        _initializationTask = _operationLifetime.RunAsync(initialize ?? InitializeAsync);
    }

    public ReactiveCollection<PackageChangeModel> InstallItems => _model.InstallItems;

    public ReactiveCollection<PackageChangeModel> UninstallItems => _model.UninstallItems;

    public ReactiveCollection<PackageChangeModel> UpdateItems => _model.UpdateItems;

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<bool> IsWaitingForTermination { get; } = new();

    public ReactiveProperty<bool> AreOthersRunning { get; } = new();

    public ReactiveProperty<PackageChangeModel> SelectedItem { get; } = new();

    internal Task InitializationTask => _initializationTask;

    // Beutl is restarting, so the changes and the clean-up after them start without waiting for Start.
    internal bool StartsAutomatically => _startsAutomatically;

    // Whether Start is pressed for the user: Beutl is restarting, the changes were read without a failure,
    // and these tools are neither closing nor only asking to be closed for another package-tools process.
    internal async Task<bool> ShouldStartAutomaticallyAsync()
    {
        if (!_startsAutomatically)
            return false;

        await _initializationTask;
        return !_initializationFailed && !_disposeRequested && !AreOthersRunning.Value;
    }

    // The user asked at the end to start Beutl; it starts once these tools are closed and disposed.
    internal void StartBeutlWhenClosed()
    {
        _startBeutlWhenClosed = true;
    }

    internal Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        Action completion,
        CancellationToken cancellationToken)
        => _operationLifetime.RunAsync(operation, completion, cancellationToken);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            AreOthersRunning.Value = _pkgProcesses.Length > 1;
            if (AreOthersRunning.Value)
                return;

            await WaitForTermination(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            IsBusy.Value = true;
            try
            {
                await _app.RestoreUserAsync(null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during authentication");
            }

            cancellationToken.ThrowIfCancellationRequested();
            (string[] installItems, string[] uninstallItems, string[] updateItems, bool launchDebugger, _) = ParseArgs();

            if (!Debugger.IsAttached && launchDebugger)
            {
                // The attach loop blocks until a debugger connects; keep it off the UI
                // thread so the window can still paint and close while waiting.
                await Task.Run(() => AttachDebugger(cancellationToken), cancellationToken);
            }

            PackageChangesQueue queue = _app.GetResource<PackageChangesQueue>();
            await _model.Load(
                _app,
                [.. installItems, .. queue.GetInstalls().Select(PackageChangesQueue.ToArgument)],
                [.. uninstallItems, .. queue.GetUninstalls().Select(PackageChangesQueue.ToArgument)],
                updateItems,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _viewModels.AddRange(InstallItems.Concat(UpdateItems)
                .Concat(UninstallItems)
                .Select(i => i.CreateViewModel(_app, _model))
                .Where(i => i != null)!);
            foreach (ActionViewModel viewModel in _viewModels)
            {
                RemoveOnceRun(viewModel, queue);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize package tools");
            // Some changes may be missing, so the user decides whether to go on.
            _initializationFailed = true;
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    // A canceled change, or one that never ran, stays queued for the next hand-off.
    internal static void RemoveOnceRun(ActionViewModel viewModel, PackageChangesQueue queue)
    {
        var package = new PackageIdentity(viewModel.Model.Id, viewModel.Model.Version);
        PackageChangesQueue.EventType type = viewModel.Model.Action == PackageChangeAction.Uninstall
            ? PackageChangesQueue.EventType.Uninstall
            : PackageChangesQueue.EventType.Install;
        viewModel.Succeeded.CombineLatest(viewModel.Failed, (succeeded, failed) => succeeded || failed)
            .Where(ran => ran)
            .Take(1)
            .Subscribe(_ => queue.Remove(package, type));
    }

    private async ValueTask WaitForTermination(CancellationToken cancellationToken)
    {
        if (_beutlProcesses.Length == 0)
        {
            return;
        }

        IsWaitingForTermination.Value = true;
        try
        {
            foreach (Process item in _beutlProcesses)
            {
                if (!item.HasExited)
                {
                    await item.WaitForExitAsync(cancellationToken);
                }
            }
        }
        finally
        {
            IsWaitingForTermination.Value = false;
        }
    }

    [Conditional("DEBUG")]
    private static void AttachDebugger(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(100);

            if (Debugger.Launch())
                break;
        }
    }

    private static (string[] InstallItems, string[] UninstallItems, string[] UpdateItems, bool LaunchDebugger, bool Restart) ParseArgs()
    {
        var command = new RootCommand();
        var installs = new Option<string[]>("--installs", "-i")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var uninstalls = new Option<string[]>("--uninstalls", "-r")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var updates = new Option<string[]>("--updates", "-u")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var launchDebugger = new Option<bool>("--launch-debugger")
        {
            Hidden = true
        };
        var sessionId = new Option<string?>("--session-id")
        {
            Hidden = true,
        };
        // Beutl passes this when the user chose to restart it.
        var restart = new Option<bool>("--restart")
        {
            Hidden = true,
        };
        command.Add(installs);
        command.Add(uninstalls);
        command.Add(updates);
        command.Add(launchDebugger);
        command.Add(sessionId);
        command.Add(restart);

        ParseResult parseResult = command.Parse(Environment.GetCommandLineArgs());
        string[] installItems = parseResult.GetValue(installs) ?? [];
        string[] uninstallItems = parseResult.GetValue(uninstalls) ?? [];
        string[] updateItems = parseResult.GetValue(updates) ?? [];
        bool launchDebuggerValue = parseResult.GetValue(launchDebugger);
        bool restartValue = parseResult.GetValue(restart);

        return (installItems, uninstallItems, updateItems, launchDebuggerValue, restartValue);
    }

    // Starts the Beutl installed beside these tools.
    private static void StartBeutl() => Process.Start(DotNetProcess.CreateBesideApplication("Beutl"))?.Dispose();

    // Beutl starts after everything these tools hold is released, so nothing they do overlaps its start.
    private async ValueTask DisposeThenStartBeutlAsync(Func<ValueTask> dispose)
    {
        try
        {
            await dispose();
        }
        finally
        {
            if (_startBeutlWhenClosed)
            {
                try
                {
                    _startBeutl();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start Beutl");
                }
            }
        }
    }

    public object? Next(ActionViewModel? current, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            ActionViewModel? vm = null;
            int index = current != null ? _viewModels.IndexOf(current) : -1;

            if (index + 1 < _viewModels.Count)
            {
                vm = _viewModels[index + 1];
            }

            if (vm != null)
            {
                return vm;
            }

            if (_cleanViewModel == null)
            {
                _cleanViewModel = new CleanViewModel(_app);
                if (_cleanViewModel.Items.Length > 0 || _cleanViewModel.RegistryUnavailable)
                {
                    return _cleanViewModel;
                }
            }
        }

        return Result();
    }

    public ResultViewModel Result()
    {
        return new ResultViewModel(
            install: [.. _viewModels.Where(x => x is { Model.Action: PackageChangeAction.Install })],
            uninstall: [.. _viewModels.Where(x => x is { Model.Action: PackageChangeAction.Uninstall })],
            update: [.. _viewModels.Where(x => x is { Model.Action: PackageChangeAction.Update })],
            clean: _cleanViewModel?.Items?.Length > 0 ? _cleanViewModel : null);
    }

    public ValueTask DisposeAsync()
    {
        _disposeRequested = true;
        return _operationLifetime.DisposeAsync();
    }

    private async ValueTask DisposeOwnedResources()
    {
        foreach (Process process in _beutlProcesses.Concat(_pkgProcesses))
        {
            try
            {
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose a tracked process handle");
            }
        }

        try
        {
            await _app.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the package-tools API application");
        }
        finally
        {
            _httpClient.Dispose();
            IsBusy.Dispose();
            IsWaitingForTermination.Dispose();
            AreOthersRunning.Dispose();
            SelectedItem.Dispose();
        }
    }
}
