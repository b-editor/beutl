using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using AvaPoint = Avalonia.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    // Todo: Refactor
    private async void OnFrameDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlayerViewModel { Scene: { } scene, EditViewModel: { } editViewModel } viewModel) return;
        TimeSpan frame = viewModel.CurrentFrame.Value;

        AvaPoint position = e.GetPosition(image);
        double scaleX = image.Bounds.Size.Width / scene.FrameSize.Width;
        Point scaledPosition = (position / scaleX).ToBtlPoint();
        Point centerePosition = scaledPosition - new Point(scene.FrameSize.Width / 2f, scene.FrameSize.Height / 2f);

        bool containsFe = e.DataTransfer.Contains(BeutlDataFormats.FilterEffect);
        bool containsTra = e.DataTransfer.Contains(BeutlDataFormats.Transform);
        if (containsFe || containsTra)
        {
            Drawable? drawable = await RenderThread.Dispatcher.InvokeAsync(() =>
                editViewModel.Renderer.Value.HitTest(
                    new((float)scaledPosition.X, (float)scaledPosition.Y)));

            if (drawable != null)
            {
                int zindex = (drawable as DrawableDecorator)?.OriginalZIndex ?? drawable.ZIndex;

                Element? element = scene.Children.FirstOrDefault(v =>
                    v.ZIndex == zindex
                    && v.Start <= frame
                    && frame < v.Range.End);

                if (element != null)
                {
                    editViewModel.SelectedObject.Value = element;
                }

                if (containsFe
                    && e.DataTransfer.TryGetValue(BeutlDataFormats.FilterEffect) is { } feTypeName
                    && TypeFormat.ToType(feTypeName) is { } feType
                    && Activator.CreateInstance(feType) is FilterEffect newFe)
                {
                    FilterEffect? fe = drawable.FilterEffect;
                    AddOrSetHelper.AddOrSet(ref fe, newFe, [element], editViewModel.CommandRecorder);
                    drawable.FilterEffect = fe;
                }
                else if (containsTra
                         && e.DataTransfer.TryGetValue(BeutlDataFormats.Transform) is { } traTypeName
                            && TypeFormat.ToType(traTypeName) is { } traType
                         && Activator.CreateInstance(traType) is ITransform newTra)
                {
                    ITransform? tra = drawable.Transform;
                    AddOrSetHelper.AddOrSet(ref tra, newTra, [element], editViewModel.CommandRecorder);
                    drawable.Transform = tra;
                }

                e.Handled = true;
            }
        }
        else
        {
            int CalculateZIndex(Scene scene)
            {
                Element[] elements = scene.Children
                    .Where(item => item.Start <= frame && frame < item.Range.End)
                    .ToArray();
                return elements.Length == 0 ? 0 : elements.Max(v => v.ZIndex) + 1;
            }

            if (e.DataTransfer.TryGetValue(BeutlDataFormats.SourceOperator) is { } typeName
                && TypeFormat.ToType(typeName) is { } type)
            {
                e.Handled = true;

                int zindex = CalculateZIndex(scene);

                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    var desc = new ElementDescription(frame, TimeSpan.FromSeconds(5), zindex, InitialOperator: type, Position: centerePosition);
                    var dialogViewModel = new AddElementDialogViewModel(scene, desc, editViewModel.CommandRecorder);
                    var dialog = new AddElementDialog { DataContext = dialogViewModel };
                    await dialog.ShowAsync();
                }
                else
                {
                    editViewModel.AddElement(new ElementDescription(
                        frame, TimeSpan.FromSeconds(5), zindex, InitialOperator: type, Position: centerePosition));
                }
            }
            else if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } fileName)
            {
                int zindex = CalculateZIndex(scene);

                editViewModel.AddElement(new ElementDescription(
                    frame, TimeSpan.FromSeconds(5), zindex, FileName: fileName, Position: centerePosition));

                e.Handled = true;
            }
        }
    }

    private void OnFrameDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.SourceOperator)
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
