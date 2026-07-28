using Avalonia;
using Avalonia.Controls;
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
