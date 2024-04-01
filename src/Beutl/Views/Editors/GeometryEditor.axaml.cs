using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

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
    }

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                try
                {
                    viewModel.AddItem();
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError("Error", ex.Message);
                }
            }
            else
            {
                //expandToggle.ContextFlyout?.ShowAt(expandToggle);
            }
        }
    }

    private async void ImportFromSvgPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel viewModel) return;

        var dialog = new ContentDialog()
        {
            Title = Strings.ImportSvgPath,
            PrimaryButtonText = Strings.Import,
            CloseButtonText = Strings.Cancel
        };
        var stack = new StackPanel()
        {
            Spacing = 8
        };
        var description = new TextBlock()
        {
            Text = Strings.ImportSvgPath_Description
        };
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
                NotificationService.ShowWarning(Strings.ImportSvgPath, Message.PleaseEnterString);
                return;
            }

            try
            {
                var obj = PathGeometry.Parse(path);
                viewModel.SetValue(viewModel.Value.Value, obj);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while parsing the SVG path.");
                NotificationService.ShowError(
                    Message.An_exception_occurred_while_parsing_the_SVG_path,
                    ex.Message);
            }
        }
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel
            && sender is MenuFlyoutItem item
            && viewModel.IsGroup.Value)
        {
            try
            {
                viewModel.AddItem();
            }
            catch (Exception ex)
            {
                NotificationService.ShowError("Error", ex.Message);
            }
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel)
        {
            viewModel.SetNull();
        }
    }
}
