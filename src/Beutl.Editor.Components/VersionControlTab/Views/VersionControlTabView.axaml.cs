using Avalonia;
using Avalonia.Controls;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Extensibility;

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
}
