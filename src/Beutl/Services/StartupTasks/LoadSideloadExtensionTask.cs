using System.Collections.Concurrent;

using Avalonia.Controls;
using Avalonia.Threading;

using Beutl.Api.Services;

using FluentAvalonia.UI.Controls;

using Serilog;

namespace Beutl.Services.StartupTasks;

public sealed class LoadSideloadExtensionTask : StartupTask
{
    private readonly ILogger _logger = Log.ForContext<LoadSideloadExtensionTask>();
    private readonly PackageManager _manager;

    public LoadSideloadExtensionTask(PackageManager manager)
    {
        _manager = manager;
        Task = Task.Run(async () =>
        {
            // .beutl/sideloads/ 内のパッケージを読み込む
            if (_manager.GetSideLoadPackages() is { Count: > 0 } sideloads
                && await ShowDialog(sideloads))
            {
                Parallel.ForEach(sideloads, item =>
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

    private static async ValueTask<bool> ShowDialog(IReadOnlyList<LocalPackage> sideloads)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = Message.DoYouWantToLoadSideloadExtensions,
                Content = new ListBox
                {
                    ItemsSource = sideloads.Select(x => x.Name).ToArray(),
                    SelectedIndex = 0
                },
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        });
    }
}
