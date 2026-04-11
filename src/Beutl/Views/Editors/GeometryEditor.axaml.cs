using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Views;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views.Editors;

public partial class GeometryEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private readonly ILogger _logger = Log.CreateLogger<GeometryEditor>();

    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;

    public GeometryEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });

        this.GetObservable(DataContextProperty)
            .Select(x => x as GeometryEditorViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<GeometryEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _fallbackObjectView = new FallbackObjectView();
                content.Children.Add(_fallbackObjectView);
            });
    }

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel { IsDisposed: false } viewModel) return;

        if (viewModel.IsGroup.Value)
        {
            try
            {
                _logger.LogInformation("Adding item to group.");
                viewModel.AddItem();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while adding item to group.");
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        }
        else
        {
            _logger.LogInformation("Group is not selected, showing context flyout.");
            expandToggle.ContextFlyout?.ShowAt(expandToggle);
        }
    }

    private async void ImportFromSvgPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel { IsDisposed: false } viewModel) return;

        _logger.LogInformation("Importing from SVG path.");
        var dialog = new ContentDialog()
        {
            Title = Strings.ImportSvgPath,
            PrimaryButtonText = Strings.Import,
            CloseButtonText = Strings.Cancel
        };
        var stack = new StackPanel() { Spacing = 8 };
        var description = new TextBlock() { Text = Strings.ImportSvgPath_Description };
        var textBox = new TextBox();

        dialog[!ContentDialog.IsPrimaryButtonEnabledProperty] = textBox.GetObservable(TextBox.TextProperty)
            .Select(s => !string.IsNullOrWhiteSpace(s))
            .ToBinding();

        stack.Children.Add(description);
        stack.Children.Add(textBox);
        dialog.Content = stack;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string? path = textBox.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("SVG path is empty.");
                NotificationService.ShowWarning(Strings.ImportSvgPath, MessageStrings.InputRequired);
                return;
            }

            try
            {
                _logger.LogInformation("Parsing SVG path.");
                var obj = PathGeometry.Parse(path);
                viewModel.SetValue(viewModel.Value.Value, obj);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while parsing the SVG path.");
                NotificationService.ShowError(
                    MessageStrings.SvgPathParsingException,
                    ex.Message);
            }
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel { IsDisposed: false } viewModel) return;

        _logger.LogInformation("Setting value to null.");
        viewModel.SetNull();
    }

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel { IsDisposed: false } viewModel) return;

        _logger.LogInformation("Initializing geometry type.");
        viewModel.ChangeGeometryType(typeof(PathGeometry));
    }

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            await vm.CopyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while copying the geometry.");
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
        try
        {
            if (!await vm.PasteAsync())
            {
                NotificationService.ShowInformation(Strings.Paste, MessageStrings.CannotPasteFromClipboard);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while pasting the geometry.");
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void CopyPasteFlyout_Opening(object? sender, EventArgs e)
    {
        if (DataContext is BaseEditorViewModel { IsDisposed: false } vm)
        {
            await vm.RefreshCanPasteAsync();
        }
    }
}
