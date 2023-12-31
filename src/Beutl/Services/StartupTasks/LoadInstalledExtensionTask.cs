using System.Collections.Concurrent;

using Avalonia.Threading;

using Beutl.Api.Services;

using FluentAvalonia.UI.Controls;

using OpenTelemetry.Trace;

using Serilog;

namespace Beutl.Services.StartupTasks;

public sealed class LoadInstalledExtensionTask : StartupTask
{
    private readonly ILogger _logger = Log.ForContext<LoadInstalledExtensionTask>();
    private readonly PackageManager _manager;

    public LoadInstalledExtensionTask(PackageManager manager)
    {
        _manager = manager;

        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadInstalledExtensionTask"))
            {
                // .beutl/packages/ 内のパッケージを読み込む
                if (!await AsksRunInRestrictedMode())
                {
                    IReadOnlyList<LocalPackage> packages = await _manager.GetPackages();

                    activity?.AddEvent(new ActivityEvent("Loading_InstalledPackages"));

                    Parallel.ForEach(packages, item =>
                    {
                        try
                        {
                            _manager.Load(item);
                        }
                        catch (Exception e)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error);
                            activity?.RecordException(e);
                            _logger.Error(e, "Failed to load package");
                            Failures.Add((item, e));
                        }
                    });

                    activity?.AddEvent(new ActivityEvent("Loaded_InstalledPackages"));
                }
            }
        });
    }

    public override Task Task { get; }

    public ConcurrentBag<(LocalPackage, Exception)> Failures { get; } = [];

    // 最後に実行したとき、例外が発生して終了した場合、
    // 制限モード (拡張機能を読み込まない) で起動するかを尋ねる。
    private static async ValueTask<bool> AsksRunInRestrictedMode()
    {
        if (UnhandledExceptionHandler.LastExecutionExceptionWasThrown())
        {
            await App.WaitWindowOpened();
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ContentDialog()
                {
                    Title = Message.Looks_like_it_ended_with_an_error_last_time,
                    Content = Message.Ask_if_you_want_to_run_in_restricted_mode,
                    PrimaryButtonText = Strings.Yes,
                    CloseButtonText = Strings.No
                };

                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            });
        }
        else
        {
            return false;
        }
    }
}
