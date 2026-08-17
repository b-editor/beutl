using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;


public partial class UninstallPage : PackageToolPage
{
    private readonly Lazy<Control> _buttons;

    private readonly Lazy<Control> _cancelButton;

    private CancellationTokenSource? _cts;

    public UninstallPage()
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
    }

    private async void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
        if (e.Parameter is UninstallViewModel)
        {
            DataContext = e.Parameter;
        }

        if (DataContext is UninstallViewModel viewModel)
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
                Frame? frame = this.FindAncestorOfType<Frame>();
                if (frame is not { DataContext: MainViewModel main })
                    return;

                try
                {
                    await main.RunOperationAsync(
                        operationToken => Task.Run(() => viewModel.Run(operationToken)),
                        () =>
                        {
                            // Navigation must run on the UI thread; the completion callback
                            // may be invoked from a thread-pool thread after Task.Run.
                            Dispatcher.UIThread.Invoke(() =>
                            {
                                object? nextViewModel = main.Next(viewModel, token);
                                frame.NavigateFromObject(nextViewModel);
                            });
                        },
                        token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }
}
