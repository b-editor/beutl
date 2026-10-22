using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal sealed partial class VersionControlTabView : UserControl
{
    private const double WideLayoutMinimumWidth = 600;

    public static readonly StyledProperty<bool> IsNarrowLayoutProperty =
        AvaloniaProperty.Register<VersionControlTabView, bool>(
            nameof(IsNarrowLayout),
            defaultValue: true);

    internal VersionControlPickerFlyout PromptFlyout { get; } = new();

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
            viewModel.RequestBranchNameAsync = RequestBranchNameAsync;
            viewModel.RequestRemoteUrlAsync = RequestRemoteUrlAsync;
            viewModel.LaunchUriAsync = LaunchUriAsync;
        }

        UpdateLayoutMode(Bounds.Width);
    }

    private void UpdateLayoutMode(double availableWidth)
    {
        IsNarrowLayout = availableWidth < WideLayoutMinimumWidth;
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

    // The main window's command enables it, as the menu does; a floating tab's window holds its dock.
    private async Task ExecuteEnableVersionControlAsync()
    {
        if (AppHelper.GetTopLevel()?.DataContext is { } window
            && AppHelper.GetContextCommandManager?.Invoke()?.GetDefinitions()
                .Select(entry => entry.Definition)
                .FirstOrDefault(definition => definition.Id == "main.enableVersionControl") is { } definition)
        {
            var execution = new ContextCommandExecution(definition.Id);
            if (definition.CanRun(window, execution))
            {
                await definition.RunAsync(window, execution);
            }
        }
    }

    private Task<bool> LaunchUriAsync(Uri uri)
    {
        return TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri)
               ?? Task.FromResult(false);
    }

    private Task<string?> RequestBranchNameAsync(CommitInfo commit)
    {
        PrimaryActionSplitButton.Flyout?.Hide();
        return PromptFlyout.ShowTextInputAsync(
            PrimaryActionSplitButton,
            Strings.VersionControl_CreateBranchTitle,
            Strings.VersionControl_BranchName,
            $"restore-{commit.ShortSha}");
    }

    private Task<string?> RequestRemoteUrlAsync(
        string? currentRemoteUrl,
        CancellationToken cancellationToken)
    {
        PrimaryActionSplitButton.Flyout?.Hide();
        return PromptFlyout.ShowTextInputAsync(
            PrimaryActionSplitButton,
            Strings.VersionControl_SetRemoteTitle,
            Strings.VersionControl_RemoteUrl,
            currentRemoteUrl,
            cancellationToken);
    }
}
