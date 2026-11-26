using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views;

public sealed partial class MainView
{
    private async Task OnExportSubtitles()
    {
        if (TopLevel.GetTopLevel(this) is not Window window
            || DataContext is not MainViewModel { EnabledSceneEditor: { Scene: { } scene } editor } viewModel
            || IsDialogOpen())
        {
            return;
        }

        HashSet<Element> selection = [.. editor.GetRequiredService<IEditorSelection>().SelectedElements];
        var dialog = new ExportSubtitlesDialog(scene, selection.Count > 0);
        bool IsActive() => DataContext == viewModel && TopLevel.GetTopLevel(this) == window
            && viewModel.EnabledSceneEditor == editor && editor.Scene == scene;

        try
        {
            if (await ShowMainDialogAsync(dialog) != FAContentDialogResult.Primary || !IsActive())
                return;

            var format = dialog.Format;
            HashSet<int> layers = dialog.GetSelectedLayers();
            IEnumerable<Element> elements = dialog.UsesSelection
                ? scene.Children.Where(selection.Contains)
                : scene.Children.Where(element => layers.Contains(element.ZIndex.Value));
            (CaptionDocument document, int skipped) = SceneCaptionExporter.BuildDocument(elements, format.Id);
            string? skippedMessage = skipped > 0 ? string.Format(Strings.ExportSubtitles_SkippedClips, skipped) : null;
            if (document.Count == 0)
            {
                string message = skippedMessage is null ? Strings.ExportSubtitles_NoText
                    : Strings.ExportSubtitles_NoText + Environment.NewLine + skippedMessage;
                Notifications.ShowWarning(Strings.ExportSubtitles, message);
                return;
            }

            byte[] bytes = viewModel.CaptionSerializer.Export(document, format.Id);
            string name = Path.GetFileNameWithoutExtension(scene.Uri?.LocalPath) ?? "subtitles";
            using IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.ExportSubtitles,
                SuggestedFileName = $"{name}.{format.Extension}",
                DefaultExtension = format.Extension,
                FileTypeChoices = [new FilePickerFileType(format.Name) { Patterns = [$"*.{format.Extension}"] }],
            });
            if (file is null || !IsActive())
                return;

            await CaptionExportStorage.WriteAsync(file, bytes, CancellationToken.None);
            if (skippedMessage is null)
                Notifications.ShowSuccess(Strings.ExportSubtitles, MessageStrings.OperationCompletedSuccessfully);
            else
                Notifications.ShowWarning(Strings.ExportSubtitles, skippedMessage);
        }
        catch (CaptionExportException ex)
        {
            Notifications.ShowError(Strings.ExportSubtitles, ex.Message);
        }
        catch (Exception ex)
        {
            await ex.Handle(Notifications);
        }
    }
}
