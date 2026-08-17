using System.CommandLine;
using System.CommandLine.Parsing;

using Beutl.Logging;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.Services;

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
            ])
    {
    }

    internal MainViewModel(
        HttpClient httpClient,
        BeutlApiApplication app,
        ChangesModel model,
        Process[] beutlProcesses,
        Process[] pkgProcesses,
        Func<CancellationToken, Task>? initialize = null,
        Action? cancelPendingRequests = null,
        Func<ValueTask>? disposeResources = null)
    {
        _httpClient = httpClient;
        _app = app;
        _model = model;
        _beutlProcesses = beutlProcesses;
        _pkgProcesses = pkgProcesses;

        _operationLifetime = new AsyncOperationLifetime(
            cancelPendingRequests ?? _httpClient.CancelPendingRequests,
            disposeResources ?? DisposeOwnedResources);
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
            (string[] installItems, string[] uninstallItems, string[] updateItems, bool launchDebugger) = ParseArgs();

            if (!Debugger.IsAttached && launchDebugger)
            {
                // The attach loop blocks until a debugger connects; keep it off the UI
                // thread so the window can still paint and close while waiting.
                await Task.Run(() => AttachDebugger(), cancellationToken);
            }

            await _model.Load(
                _app,
                installItems,
                uninstallItems,
                updateItems,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _viewModels.AddRange(InstallItems.Concat(UpdateItems)
                .Concat(UninstallItems)
                .Select(i => i.CreateViewModel(_app, _model))
                .Where(i => i != null)!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize package tools");
        }
        finally
        {
            IsBusy.Value = false;
        }
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
    private static void AttachDebugger()
    {
        while (true)
        {
            Thread.Sleep(100);

            if (Debugger.Launch())
                break;
        }
    }

    private static (string[] InstallItems, string[] UninstallItems, string[] UpdateItems, bool LaunchDebugger) ParseArgs()
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
        command.Add(installs);
        command.Add(uninstalls);
        command.Add(updates);
        command.Add(launchDebugger);
        command.Add(sessionId);

        ParseResult parseResult = command.Parse(Environment.GetCommandLineArgs());
        string[] installItems = parseResult.GetValue(installs) ?? [];
        string[] uninstallItems = parseResult.GetValue(uninstalls) ?? [];
        string[] updateItems = parseResult.GetValue(updates) ?? [];
        bool launchDebuggerValue = parseResult.GetValue(launchDebugger);

        return (installItems, uninstallItems, updateItems, launchDebuggerValue);
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
                if (_cleanViewModel.Items.Length > 0)
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
        => _operationLifetime.DisposeAsync();

    protected virtual async ValueTask DisposeOwnedResources()
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
