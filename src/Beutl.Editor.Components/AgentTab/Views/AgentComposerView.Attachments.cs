using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services.AI;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentComposerView
{
    private static async void OnOpenAttachmentImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentAttachment { HasThumbnail: true } attachment } button)
            await AgentImageLightboxWindow.OpenAsync(button, attachment.Label, attachment.Path, () => AgentImagePreview.LoadFile(attachment.Path));
    }

    private static readonly DataFormat<byte[]>[] s_encodedImageFormats =
    [
        DataFormat.CreateBytesPlatformFormat("image/png"),
        DataFormat.CreateBytesPlatformFormat("image/jpeg"),
        DataFormat.CreateBytesPlatformFormat("image/webp")
    ];

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.DataTransfer.Contains(DataFormat.File);
        bool canDrop = hasFiles && _viewModel is { CanManage: true, ShowComposer: true }
            && SceneFileDrop.GetLocalPaths(e.DataTransfer.TryGetFiles()).Length > 0;
        ComposerCard.Classes.Set("dragover", canDrop);
        if (hasFiles)
        {
            e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ComposerCard.Classes.Set("dragover", false);

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ComposerCard.Classes.Set("dragover", false);
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (_viewModel is not { CanManage: true, ShowComposer: true } model
            || SceneFileDrop.GetLocalPaths(e.DataTransfer.TryGetFiles()) is not { Length: > 0 } paths)
            return;

        e.DragEffects = DragDropEffects.Copy;
        await model.AddFilesAsync(paths);
    }

    private async void OnPromptPasting(object? sender, RoutedEventArgs e)
    {
        if (e is not PastingFromClipboardEventArgs { Clipboard: { } clipboard }
            || _viewModel is not { CanManage: true, ShowComposer: true } model)
            return;

        e.Handled = true;
        Guid? threadId = model.SelectedThread?.Id;
        bool CanApplyPaste() => ReferenceEquals(_viewModel, model) && model.SelectedThread?.Id == threadId
            && model.CanManage && model.ShowComposer && IsEffectivelyVisible && !HasActiveComposition();

        try
        {
            using var data = await clipboard.TryGetDataAsync();
            if (data is null || !CanApplyPaste())
                return;

            if (SceneFileDrop.GetLocalPaths(await data.TryGetFilesAsync()) is { Length: > 0 } paths)
            {
                if (CanApplyPaste())
                    await model.AddFilesAsync(paths);
                return;
            }

            if (await data.TryGetBitmapAsync() is { } bitmap)
            {
                if (CanApplyPaste())
                    await model.PasteImageAsync(bitmap);
                return;
            }

            foreach (var format in s_encodedImageFormats)
            {
                if (await data.TryGetValueAsync(format) is not { Length: > 0 } bytes)
                    continue;

                using var image = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    AiImageDecodeValidator.ValidateEncoded(stream, 20 * 1024 * 1024);
                    return new Bitmap(stream);
                });
                if (CanApplyPaste())
                    await model.PasteImageAsync(image);
                return;
            }

            if (await data.TryGetTextAsync() is { Length: > 0 } text && CanApplyPaste())
            {
                if (AgentAttachment.ShouldCollapsePaste(text))
                    await model.PasteTextAsync(text);
                else
                    PromptBox.SelectedText = text;
            }
        }
        catch (Exception ex)
        {
            if (CanApplyPaste())
                model.ReportPasteError(ex);
        }
    }
}
