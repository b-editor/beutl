using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    private CaptionTemplateId? _captionTemplateId;

    // Replaced by tests, where a dialog has no host.
    internal Func<IReadOnlyList<CaptionTemplate>, CaptionTemplate, IReadOnlyList<string>, Task<CaptionTemplate?>> ChooseCaptionTemplateAsync { get; set; }
        = CaptionTemplateChooser.ShowAsync;

    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        var storage = e.DataTransfer.TryGetValue(StorageDragData.Format);
        using var consumption = storage?.Consumption?.Claim();
        if (ViewModel == null) return;
        TimelinePanel.Cursor = Cursors.Arrow;
        TimelineTabViewModel viewModel = ViewModel;
        Scene scene = ViewModel.Scene;
        Point pt = e.GetPosition(TimelinePanel);

        SetClickedPosition(viewModel, pt);

        if (storage != null)
        {
            e.Handled = true;
            await ImportStorageDropAsync(viewModel, scene, storage);
            return;
        }

        ObjectTemplateItem? template = TryResolveDroppedTemplate(e);

        if (template != null)
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
        else if (TryGetDroppedFiles(e) is { Length: > 0 } files)
        {
            e.Handled = true;
            await AddDroppedFilesAsync(viewModel, files);
        }
    }

    // With only the default look there is nothing to ask.
    private async Task<CaptionTemplate?> ChooseDroppedCaptionTemplateAsync(string[] captions)
    {
        IReadOnlyList<CaptionTemplate> templates = CaptionFileDrop.GetTemplates();
        CaptionTemplate selected = templates.FirstOrDefault(item => item.Descriptor.Id == _captionTemplateId)
            ?? templates.First(item => item.Descriptor.Id == CaptionTemplateIds.DefaultText);
        if (templates.Count == 1) return selected;

        CaptionTemplate? chosen = await ChooseCaptionTemplateAsync(templates, selected, captions);
        if (chosen is not null) _captionTemplateId = chosen.Descriptor.Id;
        return chosen;
    }

    private static string[]? TryGetDroppedFiles(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

    private async Task AddDroppedFilesAsync(TimelineTabViewModel viewModel, string[] files)
    {
        MediaTime frame = viewModel.ClickedFrame;
        int layer = viewModel.CalculateClickedLayer();
        try
        {
            string[] captions = [.. files.Where(CaptionFileDrop.IsCaptionFile)];
            CaptionTemplate? template = null;
            if (captions.Length > 0)
            {
                template = await ChooseDroppedCaptionTemplateAsync(captions);
                // Cancelling gives up the whole drop, along with any files dropped beside the subtitles.
                if (template is null) return;
            }

            // The layout can have to open the files.
            ElementDescription[] descriptions = await Task.Run(
                () => FileDropLayout.Create(files.Except(captions), frame, s_defaultElementLength, layer));
            if (descriptions.Length > 0)
            {
                await viewModel.AddElementsWithResultAsync(descriptions);
            }
            else if (captions.Length == 0)
            {
                // With no media among them, adding the first says why it cannot be added.
                await viewModel.AddElementWithResultAsync(
                    new ElementDescription(frame, s_defaultElementLength, layer, new ElementSource.File(files[0])));
            }

            foreach (string caption in captions)
            {
                await CaptionFileDrop.AddAsync(viewModel, caption, template);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError); }
    }

    private static async Task ImportStorageDropAsync(TimelineTabViewModel viewModel, Scene scene, StorageDragData storage)
    {
        MediaTime dropFrame = viewModel.ClickedFrame;
        int dropLayer = viewModel.CalculateClickedLayer();
        using var fileWrite = HostProjectFileWriteAdmission.Resolve(viewModel.EditorContext)?.TryBeginProjectFileWrite();
        if (fileWrite == null)
        {
            NotificationService.ShowWarning(Strings.CloudStorage, Strings.FileBrowser_WorkspaceBusy);
            return;
        }
        try
        {
            using var import = await storage.ImportToSceneAsync(scene);
            foreach (string path in import.Paths)
            {
                ElementAddResult result;
                if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
                    && ObjectTemplateService.Instance.TryLoadFromFile(path) is { } storageTemplate)
                {
                    result = await viewModel.AddElementWithResultAsync(ElementTemplateResolver.CreateDescription(storageTemplate, dropFrame, dropLayer));
                    if (result.IsSuccess) import.RetainAll();
                }
                else
                {
                    result = await viewModel.AddElementWithResultAsync(new ElementDescription(dropFrame, s_defaultElementLength,
                        dropLayer, new ElementSource.File(path)));
                    if (result.IsSuccess) import.Retain(path);
                }
                if (result.IsSuccess) dropLayer = checked(result.Elements.Max(element => element.ZIndex.Value) + 1);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { NotificationService.ShowError(Strings.CloudStorage, Strings.CloudStorageActionFailed); }
    }

    private static ObjectTemplateItem? TryResolveDroppedTemplate(DragEventArgs e)
    {
        ObjectTemplateItem? template = null;
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.ObjectTemplate) is { } idStr
            && Guid.TryParse(idStr, out Guid templateId))
        {
            template = ObjectTemplateService.Instance.FindById(templateId);
        }

        // テンプレートファイルのドロップもテンプレート経路で処理
        if (template == null && e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase))
        {
            template = ObjectTemplateService.Instance.TryLoadFromFile(droppedFile);
        }

        return template;
    }

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
