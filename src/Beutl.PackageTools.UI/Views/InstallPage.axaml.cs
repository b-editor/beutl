using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;


public partial class InstallPage : PackageToolPage
{
    private readonly Lazy<Control> _buttons;

    private readonly Lazy<Control> _cancelButton;

    private CancellationTokenSource? _cts;

    public InstallPage()
    {
        _buttons = new(() =>
        {
            var panel = new TaskDialogButtonsPanel
            {
                [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
                Spacing = 8
            };
            var backButton = new TaskDialogButtonHost()
            {
                Content = Strings.Back
            };
            backButton.Click += (s, e) =>
            {
                Frame? frame = this.FindAncestorOfType<Frame>();
                frame?.GoBack();
            };
            panel.Children.Add(backButton);

            return panel;
        });

        _cancelButton = new(() =>
        {
            var panel = new TaskDialogButtonsPanel
            {
                [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
                Spacing = 8
            };
            var button = new TaskDialogButtonHost()
            {
                Content = Strings.Cancel
            };
            button.Click += (_, _) =>
            {
                _cts?.Cancel();
            };
            panel.Children.Add(button);

            return panel;
        });

        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        InitializeComponent();

        // 現在のタスクに応じて、スクロールする
        this.GetObservable(DataContextProperty)
            .Select(v => (v as InstallViewModel)?.CurrentRunningTask ?? Observable.Return((object?)null))
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

    private async void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
        if (e.Parameter is InstallViewModel)
        {
            DataContext = e.Parameter;
        }

        if (DataContext is InstallViewModel viewModel)
        {
            if (viewModel.Finished.Value)
            {
                ButtonsContainer = _buttons.Value;
            }
            else
            {
                ButtonsContainer = _cancelButton.Value;
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                await Task.Run(async () => await viewModel.Run(token));
                Frame? frame = this.FindAncestorOfType<Frame>();
                if (frame is { DataContext: MainViewModel main })
                {
                    object? nextViewModel = main.Next(viewModel, token);
                    frame.NavigateFromObject(nextViewModel);
                }
            }
        }
    }
}
