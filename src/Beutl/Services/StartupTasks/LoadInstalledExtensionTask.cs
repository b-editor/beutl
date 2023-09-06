using System.Collections.Concurrent;

using Avalonia.Threading;

using Beutl.Api.Services;
using Beutl.Services;

using FluentAvalonia.UI.Controls;

using Serilog;

namespace Beutl.Services.StartupTasks;

public sealed class LoadInstalledExtensionTask : StartupTask
{
    private readonly ILogger _logger = Log.ForContext<LoadInstalledExtensionTask>();
    private readonly AuthenticationTask _authenticationTask;
    private readonly PackageManager _manager;

    public LoadInstalledExtensionTask(AuthenticationTask authenticationTask, PackageManager manager)
    {
        _authenticationTask = authenticationTask;
        _manager = manager;

        Task = Task.Run(async () =>
        {
            await _authenticationTask.Task;

            // .beutl/packages/ 内のパッケージを読み込む
            if (!await AsksRunInRestrictedMode())
            {
                Parallel.ForEach(await _manager.GetPackages(), item =>
                {
                    try
                    {
                        _manager.Load(item);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to load package");
                        Failures.Add((item, e));
                    }
                });
            }
        });
    }

    public override Task Task { get; }

    public ConcurrentBag<(LocalPackage, Exception)> Failures { get; } = new();

    // 最後に実行したとき、例外が発生して終了した場合、
    // 制限モード (拡張機能を読み込まない) で起動するかを尋ねる。
    private async ValueTask<bool> AsksRunInRestrictedMode()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (UnhandledExceptionHandler.LastExecutionExceptionWasThrown())
            {
                var dialog = new ContentDialog()
                {
                    Title = Message.Looks_like_it_ended_with_an_error_last_time,
                    Content = Message.Ask_if_you_want_to_run_in_restricted_mode,
                    PrimaryButtonText = Strings.Yes,
                    CloseButtonText = Strings.No
                };

                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }
            else
            {
                return false;
            }
        });
    }
}
