using Avalonia.Input;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaPoint = Avalonia.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    private readonly CaptionTemplateChooser _captionTemplateChooser = new();

    private async void OnFrameDrop(object? sender, DragEventArgs e)
    {
        var storage = e.DataTransfer.TryGetValue(StorageDragData.Format);
        using var consumption = storage?.Consumption?.Claim();
        if (DataContext is not PlayerViewModel { Scene: { } scene, EditViewModel: { } editViewModel } viewModel) return;
        MediaTime frame = viewModel.CurrentFrame.Value;

        if (ViewportTransform is not { } viewport) return;
        AvaPoint position = e.GetPosition(image);
        Point scaledPosition = viewport.ToFrame(position).ToBtlPoint();
        Point centeredPosition = scaledPosition - new Point(scene.FrameSize.Value.Width / 2f, scene.FrameSize.Value.Height / 2f);

        if (storage != null)
        {
            e.Handled = true;
            await DropFilesAsync(editViewModel, scene, [], frame, centeredPosition, storage);
            return;
        }

        bool containsFe = e.DataTransfer.Contains(BeutlDataFormats.FilterEffect);
        bool containsTra = e.DataTransfer.Contains(BeutlDataFormats.Transform);
        if (containsFe || containsTra)
        {
            await DropEffectOntoHitDrawableAsync(e, scene, editViewModel, frame, scaledPosition, containsFe, containsTra);
        }
        else
        {
            await DropNewElementAsync(e, scene, editViewModel, frame, centeredPosition);
        }
    }

    private async Task DropEffectOntoHitDrawableAsync(
        DragEventArgs e,
        Scene scene,
        EditViewModel editViewModel,
        MediaTime frame,
        Point scaledPosition,
        bool containsFe,
        bool containsTra)
    {
        Drawable? drawable = await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            var compositor = editViewModel.Renderer.Value.Compositor;
            var compositionFrame = compositor.EvaluateGraphics(frame);
            return editViewModel.Renderer.Value.HitTest(compositionFrame, new((float)scaledPosition.X, (float)scaledPosition.Y));
        });

        if (drawable != null)
        {
            // TODO: DrawableGroup以下のDrawableを拾った場合の対応
            int zindex = drawable.ZIndex.Value;

            Element? element = FindEnabledElementAt(scene, zindex, frame);

            if (element != null)
            {
                var editorSelection = editViewModel.GetService<IEditorSelection>();
                editorSelection?.Select(element);
            }

            if (containsFe
                && e.DataTransfer.TryGetValue(BeutlDataFormats.FilterEffect) is { } feTypeName
                && TypeFormat.ToType(feTypeName) is { } feType
                && Activator.CreateInstance(feType) is FilterEffect newFe)
            {
                AddToGroupHelper.Add(drawable.FilterEffect, newFe);
            }
            else if (containsTra
                     && e.DataTransfer.TryGetValue(BeutlDataFormats.Transform) is { } traTypeName
                     && TypeFormat.ToType(traTypeName) is { } traType
                     && Activator.CreateInstance(traType) is Transform newTra)
            {
                AddToGroupHelper.Add(drawable.Transform, newTra);
            }

            e.Handled = true;
        }
    }

    private async Task DropNewElementAsync(
        DragEventArgs e,
        Scene scene,
        EditViewModel editViewModel,
        MediaTime frame,
        Point centeredPosition)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } type)
        {
            if (LibraryElementDescription.Create(type, frame, MediaTime.FromSeconds(5), PlayheadPlacement.FindLayer(scene, frame),
                    centeredPosition) is { } description)
            {
                e.Handled = true;
                await AddElement(editViewModel, description);
            }
        }
        else if (SceneFileDrop.GetLocalPaths(e.DataTransfer.TryGetFiles()) is { Length: > 0 } files)
        {
            e.Handled = true;
            await DropFilesAsync(editViewModel, scene, files, frame, centeredPosition);
        }
    }

    private Task DropFilesAsync(
        EditViewModel editor, Scene scene, string[] files, MediaTime frame, Point position, StorageDragData? storage = null)
        => SceneFileDrop.AddAsync(
            editor, scene, files, frame, PlayheadPlacement.FindLayer(scene, frame), image, _captionTemplateChooser,
            editor.FindToolTab<TimelineTabViewModel>(), position, storage);

    private static Element? FindEnabledElementAt(Scene scene, int zIndex, MediaTime time)
    {
        return scene.Children.FirstOrDefault(v =>
            v.IsEnabled.Value
            && v.ZIndex.Value == zIndex
            && v.Start.Value <= time
            && time < v.Range.End);
    }

    internal async Task<ElementAddResult?> AddElement(EditViewModel editViewModel, ElementDescription description)
    {
        if (editViewModel.FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            try
            {
                return await timeline.AddElementWithResultAsync(description);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            return null;
        }

        ElementAddResult? result = await AddPlayerDropAsync(
            editViewModel.GetRequiredService<IElementAdder>(),
            description);
        if (result is null)
            return null;
        if (result.IsSuccess)
            return result;

        if (result.Failure is LockedElementLayerFailure)
        {
            Notifications.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            return result;
        }

        _logger.LogError(
            result.Failure?.Exception,
            "Failed to add a player drop: {FailureId}",
            result.Failure?.Id);
        Notifications.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
        return result;
    }

    internal static async Task<ElementAddResult?> AddPlayerDropAsync(
        IElementAdder elementAdder,
        ElementDescription description)
    {
        try
        {
            return await elementAdder.AddAsync([description], CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private void OnFrameDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(StorageDragData.Format) is { } storage)
        {
            e.DragEffects = storage.IsCurrent() ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }
        if (e.DataTransfer.Contains(BeutlDataFormats.EngineObject)
            || e.DataTransfer.Contains(BeutlDataFormats.FilterEffect)
            || e.DataTransfer.Contains(BeutlDataFormats.Transform)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
