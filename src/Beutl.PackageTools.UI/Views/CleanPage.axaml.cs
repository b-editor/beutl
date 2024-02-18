using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.Resources;
using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;


public partial class CleanPage : PackageToolPage
{
    private readonly Lazy<Control> _backButton;

    private readonly Lazy<Control> _buttons;

    private CancellationTokenSource? _cts;

    public CleanPage()
    {
        _backButton = new(() =>
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

        _buttons = new(() =>
        {
            var panel = new TaskDialogButtonsPanel
            {
                [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
                Spacing = 8
            };
            var cancelButton = new TaskDialogButtonHost()
            {
                Content = Strings.Cancel,
                IsEnabled = false
            };
            var runButton = new TaskDialogButtonHost()
            {
                Content = Strings.Start,
                Classes = { "accent" }
            };
            cancelButton.Click += (_, _) =>
            {
                _cts?.Cancel();
            };
            runButton.Click += async (_, _) =>
            {
                if (DataContext is CleanViewModel viewModel)
                {
                    _cts?.Cancel();
                    _cts = new CancellationTokenSource();
                    CancellationToken token = _cts.Token;
                    try
                    {
                        cancelButton.IsEnabled = true;
                        runButton.IsEnabled = false;
                        await Task.Run(() => viewModel.Run(token));
                        Frame? frame = this.FindAncestorOfType<Frame>();
                        if (frame is { DataContext: MainViewModel main })
                        {
                            object? nextViewModel = main.Result();
                            frame.NavigateFromObject(nextViewModel);
                        }
                    }
                    finally
                    {
                        runButton.IsEnabled = false;
                        cancelButton.IsEnabled = false;
                    }
                }
            };
            panel.Children.Add(cancelButton);
            panel.Children.Add(runButton);

            return panel;
        });

        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        InitializeComponent();
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
        if (e.Parameter is CleanViewModel)
        {
            DataContext = e.Parameter;
        }

        if (DataContext is CleanViewModel viewModel)
        {
            if (viewModel.Finished.Value)
            {
                ButtonsContainer = _backButton.Value;
            }
            else
            {
                ButtonsContainer = _buttons.Value;
            }
        }
    }
}
