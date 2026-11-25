using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    private readonly CaptionTemplateChooser _captionTemplateChooser = new();

    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        var storage = e.DataTransfer.TryGetValue(StorageDragData.Format);
        using var consumption = storage?.Consumption?.Claim();
        if (ViewModel == null) return;
        TimelinePanel.Cursor = Cursors.Arrow;
        TimelineTabViewModel viewModel = ViewModel;
        Point pt = e.GetPosition(TimelinePanel);

        SetClickedPosition(viewModel, pt);

        if (storage != null)
        {
            e.Handled = true;
            await AddDroppedFilesAsync(viewModel, [], storage);
            return;
        }

        if (e.DataTransfer.TryGetValue(BeutlDataFormats.ObjectTemplate) is { } id
            && Guid.TryParse(id, out Guid templateId)
            && viewModel.GetRequiredService<ObjectTemplateService>().FindById(templateId) is { } template)
        {
            if (ElementTemplateResolver.CanCreateDescription(template))
            {
                viewModel.AddElement.Execute(ElementTemplateResolver.CreateDescription(
                    template,
                    viewModel.ClickedFrame,
                    viewModel.CalculateClickedLayer()));
            }

            e.Handled = true;
        }
        else if (e.DataTransfer.Contains(BeutlDataFormats.ClipTransition))
        {
            // A transition dropped on an element goes to the edge nearer the pointer.
            if (e.DataTransfer.TryGetValue(BeutlDataFormats.ClipTransition) is { } transitionTypeName
                && TypeFormat.ToType(transitionTypeName) is { } transitionType
                && ElementTransitionEdits.IsTransitionType(transitionType)
                && FindTransitionDropTarget(viewModel, e, pt) is { } target)
            {
                target.Element.ApplyTransition(target.Edge, transitionType);
            }

            e.Handled = true;
        }
        else if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } type)
        {
            AddEngineObjectAtClick(viewModel, type);

            e.Handled = true;
        }
        else if (SceneFileDrop.GetLocalPaths(e.DataTransfer.TryGetFiles()) is { Length: > 0 } files)
        {
            e.Handled = true;
            await AddDroppedFilesAsync(viewModel, files);
        }
    }

    private Task AddDroppedFilesAsync(TimelineTabViewModel viewModel, string[] files, StorageDragData? storage = null)
        => SceneFileDrop.AddAsync(
            viewModel.EditorContext, viewModel.Scene, files, viewModel.ClickedFrame, viewModel.CalculateClickedLayer(),
            TimelinePanel, _captionTemplateChooser, viewModel, storage: storage);

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(StorageDragData.Format) is { } storage)
        {
            e.DragEffects = storage.IsCurrent() ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }
        if (e.DataTransfer.Contains(BeutlDataFormats.ClipTransition))
        {
            e.DragEffects = ViewModel is { } viewModel
                            && FindTransitionDropTarget(viewModel, e, e.GetPosition(TimelinePanel)) is { Element.IsEditable.Value: true }
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            return;
        }

        if (e.DataTransfer.Contains(BeutlDataFormats.ObjectTemplate)
            || e.DataTransfer.Contains(BeutlDataFormats.EngineObject)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    // The element under the pointer and the edge of it the pointer is nearer. The element whose view the
    // pointer is over wins, since clips that overlap on a layer are drawn one above the other.
    private static (ElementViewModel Element, ElementEdge Edge)? FindTransitionDropTarget(
        TimelineTabViewModel viewModel, DragEventArgs e, Point position)
    {
        MediaTime time = position.X.PixelToTime(viewModel.Options.Value.Scale);
        ElementViewModel? element = (e.Source as Visual)?.FindAncestorOfType<ElementView>(includeSelf: true)?.DataContext
            as ElementViewModel;
        if (element == null)
        {
            int layer = viewModel.ToLayerNumber(position.Y);
            Element? hit = viewModel.Scene.Children.FirstOrDefault(
                item => item.ZIndex.Value == layer && item.Range.Contains(time));
            element = hit is null ? null : viewModel.GetViewModelFor(hit);
        }

        if (element == null) return null;

        Element model = element.Model;
        MediaTime middle = model.Start.Value + new MediaTime(model.Length.Value.Flicks / 2);
        return (element, time < middle ? ElementEdge.Start : ElementEdge.End);
    }
}
