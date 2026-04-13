using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using static Beutl.Views.Editors.PropertiesEditor;

namespace Beutl.Views.Editors;

public sealed partial class PenEditor : UserControl
{
    public PenEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        ExpandTransitionHelper.Attach(expandMinorProps, minorProps);

        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)ExpandMenuButton.ContextFlyout!);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PenEditorViewModel { IsDisposed: false } viewModel) return;

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

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PenEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, new Pen());
            expandToggle.IsChecked = true;
        }
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PenEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, null);
        }
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }
}
