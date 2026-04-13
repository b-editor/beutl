using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts1;
    private CancellationTokenSource? _lastTransitionCts2;

    public PenEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts1?.Cancel();
                _lastTransitionCts1 = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts1.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });

        expandMinorProps.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts2?.Cancel();
                _lastTransitionCts2 = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts2.Token;

                if (v == true)
                {
                    await s_transition.Start(null, minorProps, localToken);
                }
                else
                {
                    await s_transition.Start(minorProps, null, localToken);
                }
            });

        CopyPasteMenuHelper.AddMenus((FAMenuFlyout)ExpandMenuButton.ContextFlyout!, this);
        TemplateMenuHelper.AddMenus((FAMenuFlyout)ExpandMenuButton.ContextFlyout!, this);

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
