using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            var panel = new FATaskDialogButtonsPanel
            {
                [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
                Spacing = 8
            };
            var backButton = new FATaskDialogButtonHost()
            {
                Content = Strings.Back
            };
            backButton.Click += (s, e) =>
            {
                FAFrame? frame = this.FindAncestorOfType<FAFrame>();
                frame?.GoBack();
            };
            panel.Children.Add(backButton);

            return panel;
        });

        _cancelButton = new(() =>
        {
            var panel = new FATaskDialogButtonsPanel
            {
                [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
                Spacing = 8
            };
            var button = new FATaskDialogButtonHost()
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

        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        InitializeComponent();
    }

    private async void OnNavigatedTo(object? sender, FANavigationEventArgs e)
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
                await Task.Run(() => viewModel.Run(token));
                FAFrame? frame = this.FindAncestorOfType<FAFrame>();
                if (frame is { DataContext: MainViewModel main })
                {
                    object? nextViewModel = main.Next(viewModel, token);
                    frame.NavigateFromObject(nextViewModel);
                }
            }
        }
    }
}
