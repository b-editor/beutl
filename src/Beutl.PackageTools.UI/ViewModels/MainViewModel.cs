using System.CommandLine;
using System.CommandLine.Parsing;

using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class MainViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly ChangesModel _model;
    private readonly BeutlApiApplication _app;

    private readonly List<ActionViewModel> _viewModels = [];

    private CleanViewModel? _cleanViewModel;

    private readonly Process[] _beutlProcesses;
    private readonly Process[] _pkgProcesses;

    public MainViewModel()
    {
        _app = new BeutlApiApplication(new HttpClient());
        _model = new ChangesModel();

        _beutlProcesses =
        [
            .. Process.GetProcessesByName("Beutl"),
            .. Process.GetProcessesByName("beutl")
        ];

        _pkgProcesses =
        [
            ..Process.GetProcessesByName("Beutl.PackageTools"),
            ..Process.GetProcessesByName("Beutl.PackageTools.UI"),
            ..Process.GetProcessesByName("beutl-pkg"),
        ];

        Task.Run(async () =>
        {
            AreOthersRunning.Value = _pkgProcesses.Length > 1;
            if (AreOthersRunning.Value)
            {
                return;
            }

            await WaitForTermination();

            IsBusy.Value = true;
            try
            {
                await _app.RestoreUserAsync(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during authentication");
            }

            (string[] installItems, string[] uninstallItems, string[] updateItems, bool launchDebugger) = ParseArgs();

            if (!Debugger.IsAttached && launchDebugger)
            {
                AttachDebugger();
            }

            try
            {
                await _model.Load(_app, installItems, uninstallItems, updateItems);

                _viewModels.AddRange(InstallItems.Concat(UpdateItems)
                    .Concat(UninstallItems)
                    .Select(i => i.CreateViewModel(_app, _model))
                    .Where(i => i != null)!);
            }
            finally
            {
                IsBusy.Value = false;
            }
        });
    }

    public ReactiveCollection<PackageChangeModel> InstallItems => _model.InstallItems;

    public ReactiveCollection<PackageChangeModel> UninstallItems => _model.UninstallItems;

    public ReactiveCollection<PackageChangeModel> UpdateItems => _model.UpdateItems;

    public ReactiveProperty<bool> IsBusy { get; } = new();

    public ReactiveProperty<bool> IsWaitingForTermination { get; } = new();

    public ReactiveProperty<bool> AreOthersRunning { get; } = new();

    public ReactiveProperty<PackageChangeModel> SelectedItem { get; } = new();

    private async ValueTask WaitForTermination()
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
                    await item.WaitForExitAsync();
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
        var installs = new Option<string[]>(["--installs", "-i"], () => [])
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var uninstalls = new Option<string[]>(["--uninstalls", "-r"], () => [])
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var updates = new Option<string[]>(["--updates", "-u"], () => [])
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var launchDebugger = new Option<bool>("--launch-debugger", () => false)
        {
            IsHidden = true,
        };
        var sessionId = new Option<string?>("--session-id", () => null)
        {
            IsHidden = true,
        };
        command.AddOption(installs);
        command.AddOption(uninstalls);
        command.AddOption(updates);
        command.AddOption(launchDebugger);
        command.AddOption(sessionId);

        ParseResult parseResult = command.Parse(Environment.GetCommandLineArgs());
        string[] installItems = parseResult.GetValueForOption(installs)!;
        string[] uninstallItems = parseResult.GetValueForOption(uninstalls)!;
        string[] updateItems = parseResult.GetValueForOption(updates)!;
        bool launchDebuggerValue = parseResult.GetValueForOption(launchDebugger);

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
}
