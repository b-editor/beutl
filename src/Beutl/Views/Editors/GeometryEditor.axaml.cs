using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views.Editors;

public partial class GeometryEditor : UserControl
{
    private readonly ILogger _logger = Log.CreateLogger<GeometryEditor>();

    public GeometryEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)expandToggle.ContextFlyout!);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not GeometryEditorViewModel { IsDisposed: false } viewModel) return;

        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } template
            && viewModel.ApplyTemplate(template))
        {
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
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
        var dialog = new FAContentDialog()
        {
            Title = Strings.ImportSvgPath,
            PrimaryButtonText = Strings.Import,
            CloseButtonText = Strings.Cancel
        };
        var stack = new StackPanel() { Spacing = 8 };
        var description = new TextBlock() { Text = Strings.ImportSvgPath_Description };
        var textBox = new TextBox();

        dialog[!FAContentDialog.IsPrimaryButtonEnabledProperty] = textBox.GetObservable(TextBox.TextProperty)
            .Select(s => !string.IsNullOrWhiteSpace(s))
            .ToBinding();

        stack.Children.Add(description);
        stack.Children.Add(textBox);
        dialog.Content = stack;

        if (await dialog.ShowAsync() == FAContentDialogResult.Primary)
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
}
