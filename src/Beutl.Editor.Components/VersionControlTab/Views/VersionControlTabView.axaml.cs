using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Language;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.VersionControlTab.Views;

public sealed partial class VersionControlTabView : UserControl
{
    public static readonly StyledProperty<bool> IsNarrowLayoutProperty =
        AvaloniaProperty.Register<VersionControlTabView, bool>(
            nameof(IsNarrowLayout),
            defaultValue: true);

    public VersionControlTabView()
    {
        InitializeComponent();
        AddHandler(
            KeyDownEvent,
            OnCommitMessageKeyDown,
            RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        ConfigureCallbacks();
    }

    public bool IsNarrowLayout
    {
        get => GetValue(IsNarrowLayoutProperty);
        private set => SetValue(IsNarrowLayoutProperty, value);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateLayoutMode(e.NewSize.Width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        ConfigureCallbacks();
    }

    private void ConfigureCallbacks()
    {
        if (DataContext is VersionControlTabViewModel viewModel)
        {
            viewModel.RequestEnableVersionControlAsync = ExecuteEnableVersionControlAsync;
            viewModel.RequestRemoteUrlAsync = RequestRemoteUrlAsync;
            viewModel.LaunchUriAsync = LaunchUriAsync;
        }

        UpdateLayoutMode(Bounds.Width);
    }

    private void UpdateLayoutMode(double availableWidth)
    {
        IsNarrowLayout = VersionControlTabLayout.IsNarrow(availableWidth);
    }

    private void OnCommitMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (!CommitMessageTextBox.IsKeyboardFocusWithin)
        {
            return;
        }

        ICommand? commitCommand =
            (DataContext as VersionControlTabViewModel)?.CommitCommand;
        if (TryExecuteCommitShortcut(e.Key, e.KeyModifiers, commitCommand))
        {
            e.Handled = true;
        }
    }

    internal static bool TryExecuteCommitShortcut(
        Key key,
        KeyModifiers modifiers,
        ICommand? command)
    {
        if (key is not (Key.Enter or Key.Return)
            || modifiers is not (KeyModifiers.Control or KeyModifiers.Meta))
        {
            return false;
        }

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }

        return true;
    }

    private Task ExecuteEnableVersionControlAsync()
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is IContextCommandHandler handler)
        {
            var execution = new ContextCommandExecution("EnableVersionControl");
            if (handler.CanExecute(execution))
            {
                handler.Execute(execution);
            }
        }

        return Task.CompletedTask;
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri)
               ?? Task.FromResult(false);
    }

    private static async Task<string?> RequestRemoteUrlAsync(string? currentRemoteUrl)
    {
        var textBox = new TextBox
        {
            Text = currentRemoteUrl,
            Watermark = Strings.VersionControl_RemoteUrl,
        };
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_SetRemoteTitle,
            Content = textBox,
            PrimaryButtonText = Strings.OK,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog[!ContentDialog.IsPrimaryButtonEnabledProperty] =
            textBox.GetObservable(TextBox.TextProperty)
                .Select(static value => !string.IsNullOrWhiteSpace(value))
                .ToBinding();

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }
}
