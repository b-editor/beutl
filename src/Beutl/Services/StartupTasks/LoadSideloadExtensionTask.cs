using System.Collections.Concurrent;

using Beutl.Api.Services;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class LoadSideloadExtensionTask : StartupTask
{
    private readonly ILogger _logger;
    private readonly Action<LocalPackage> _load;
    private readonly Func<IReadOnlyList<string>, Task<bool>> _confirm;
    private readonly Action<IReadOnlyList<(LocalPackage, Exception)>> _showFailures;
    private Task<bool>? _confirmationTask;
    private Task _deferredLoadingTask = System.Threading.Tasks.Task.CompletedTask;

    public LoadSideloadExtensionTask(PackageManager manager)
        : this(
            manager.GetSideLoadPackages,
            package => manager.Load(package),
            StartupNotificationService.ConfirmSideloadExtensions,
            AfterLoadingExtensionsTask.ShowFailures,
            Log.CreateLogger<LoadSideloadExtensionTask>())
    {
    }

    internal LoadSideloadExtensionTask(
        Func<IReadOnlyList<LocalPackage>> getSideloads,
        Action<LocalPackage> load,
        Func<IReadOnlyList<string>, Task<bool>> confirm,
        Action<IReadOnlyList<(LocalPackage, Exception)>> showFailures,
        ILogger logger)
    {
        _load = load;
        _confirm = confirm;
        _showFailures = showFailures;
        _logger = logger;
        Task = System.Threading.Tasks.Task.Run(() =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadSideloadExtensionTask"))
            {
                // .beutl/sideloads/ 内のパッケージを読み込む
                if (getSideloads() is { Count: > 0 } sideloads)
                {
                    activity?.AddEvent(new ActivityEvent("Done_GetSideLoadPackages"));
                    Task<bool> confirmation = _confirm(sideloads.Select(x => x.Name).ToArray());
                    _confirmationTask = confirmation;
                    _deferredLoadingTask = System.Threading.Tasks.Task.Run(
                        () => LoadAfterConfirmation(sideloads, confirmation));
                }
            }
        });
    }

    public override Task Task { get; }

    internal Task DeferredLoadingTask => _deferredLoadingTask;

    public ConcurrentBag<(LocalPackage, Exception)> Failures { get; } = [];

    internal async Task WaitForAcceptedLoadingAsync()
    {
        await Task.ConfigureAwait(false);

        if (_confirmationTask?.IsCompleted == true)
        {
            await _deferredLoadingTask.ConfigureAwait(false);
        }
    }

    private async Task LoadAfterConfirmation(IReadOnlyList<LocalPackage> sideloads, Task<bool> confirmation)
    {
        bool accepted;
        try
        {
            accepted = await confirmation.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to confirm loading side-load-packages.");
            return;
        }

        if (!accepted)
        {
            _logger.LogWarning("User canceled loading side-load-packages.");
            return;
        }

        using Activity? activity = Telemetry.StartActivity("LoadSideloadExtensionsAfterConfirmation");
        activity?.AddEvent(new ActivityEvent("Started loading side-load-packages."));

        Parallel.ForEach(sideloads, item =>
        {
            try
            {
                _load(item);
            }
            catch (Exception e)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogError(e, "Failed to load package: {PackageName}", item.Name);
                Failures.Add((item, e));
            }
        });

        activity?.AddEvent(new ActivityEvent("Finished loading side-load-packages."));

        if (!Failures.IsEmpty)
        {
            try
            {
                _showFailures(Failures.ToArray());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to show side-load package failures.");
            }
        }
    }
}
