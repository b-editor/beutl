using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;
using Beutl.Reactive;

namespace Beutl.PackageTools.UI.Views;


public partial class InstallPage : PackageToolPage
{
    public InstallPage()
    {
        InitializeComponent();
        PackageActionNavigation.HandleNavigatedTo<InstallViewModel>(
            this,
            Scroll,
            (viewModel, token) => Task.Run(() => viewModel.Run(token)));
        FollowCurrentTask();
    }

    private void FollowCurrentTask()
    {
        // 現在のタスクに応じて、スクロールする
        this.GetObservable(DataContextProperty)
            .Select(v => (v as InstallViewModel)?.CurrentRunningTask ?? Observable.ReturnThenNever<object?>(null))
            .Switch()
            .Select(obj => obj switch
            {
                DownloadTaskModel => DownloadView,
                VerifyTaskModel => VerifyView,
                ResolveTaskModel => ResolveView,
                AcceptLicenseTaskModel => AcceptLicenseView,
                _ => (Control?)null,
            })
            .Where(c => c != null)
            .Select(c => Observable.FromEventPattern<SizeChangedEventArgs>(h => c!.SizeChanged += h, h => c!.SizeChanged -= h)
                .Select(e => (Control?)e.Sender)
                .Publish(c)
                .RefCount())
            .Switch()
            .Subscribe(control =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Scroll.Content is Visual c)
                    {
                        Matrix? mat = control?.TransformToVisual(c);

                        if (mat.HasValue)
                        {
                            double y = mat.Value.M32;
                            if (Scroll.Offset.Y < y)
                            {
                                Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, y));
                            }
                        }
                    }
                }, DispatcherPriority.Background);
            });
    }
}
