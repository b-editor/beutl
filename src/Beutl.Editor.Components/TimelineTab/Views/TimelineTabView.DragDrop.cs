using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    // ドロップされた
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
            if (typeof(Element).IsAssignableFrom(template.ActualType)
                || typeof(EngineObject).IsAssignableFrom(template.ActualType))
            {
                viewModel.AddElement.Execute(ElementTemplateResolver.CreateDescription(
                    template,
                    viewModel.ClickedFrame,
                    viewModel.CalculateClickedLayer()));
            }

            e.Handled = true;
        }
        else if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } type)
        {
            AddEngineObjectAtClick(viewModel, type);

            e.Handled = true;
        }
        else if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } fileName)
        {
            viewModel.AddElement.Execute(new ElementDescription(
                viewModel.ClickedFrame, s_defaultElementLength, viewModel.CalculateClickedLayer(),
                new ElementSource.File(fileName)));

            e.Handled = true;
        }
    }

    private static async Task ImportStorageDropAsync(TimelineTabViewModel viewModel, Scene scene, StorageDragData storage)
    {
        TimeSpan dropFrame = viewModel.ClickedFrame;
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
                if (result.IsSuccess) dropLayer = checked(result.Elements.Max(element => element.ZIndex) + 1);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { NotificationService.ShowError(Strings.CloudStorage, Strings.CloudStorageActionFailed); }
    }

    private static ObjectTemplateItem? TryResolveDroppedTemplate(DragEventArgs e)
    {
        // テンプレート経路（優先）
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
        if (e.DataTransfer.Contains(BeutlDataFormats.ObjectTemplate)
            || e.DataTransfer.Contains(BeutlDataFormats.EngineObject)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }
}
