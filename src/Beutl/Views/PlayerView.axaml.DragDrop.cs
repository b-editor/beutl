using Avalonia.Input;
using Avalonia.Platform.Storage;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
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
        Point centerePosition = scaledPosition - new Point(scene.FrameSize.Width / 2, scene.FrameSize.Height / 2);

        if (e.Data.Contains(KnownLibraryItemFormats.FilterEffect)
            || e.Data.Contains(KnownLibraryItemFormats.Transform))
        {
            Drawable? drawable = editViewModel.Renderer.Value.HitTest(new((float)scaledPosition.X, (float)scaledPosition.Y));

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

                if (e.Data.Get(KnownLibraryItemFormats.FilterEffect) is Type feType
                    && Activator.CreateInstance(feType) is FilterEffect newFe)
                {
                    FilterEffect? fe = drawable.FilterEffect;
                    AddOrSetHelper.AddOrSet(ref fe, newFe, [element], editViewModel.CommandRecorder);
                    drawable.FilterEffect = fe;
                }
                else if (e.Data.Get(KnownLibraryItemFormats.Transform) is Type traType
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

            if (e.Data.Get(KnownLibraryItemFormats.SourceOperator) is Type type)
            {
                e.Handled = true;

                int zindex = CalculateZIndex(scene);

                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    var desc = new ElementDescription(frame, TimeSpan.FromSeconds(5), zindex, InitialOperator: type, Position: centerePosition);
                    var dialogViewModel = new AddElementDialogViewModel(scene, desc, editViewModel.CommandRecorder);
                    var dialog = new AddElementDialog
                    {
                        DataContext = dialogViewModel
                    };
                    await dialog.ShowAsync();
                }
                else
                {
                    editViewModel.AddElement(new ElementDescription(
                        frame, TimeSpan.FromSeconds(5), zindex, InitialOperator: type, Position: centerePosition));
                }
            }
            else if (e.Data.GetFiles()
                ?.Where(v => v is IStorageFile)
                ?.Select(v => v.TryGetLocalPath())
                .FirstOrDefault(v => v != null) is { } fileName)
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
        if (e.Data.Contains(KnownLibraryItemFormats.SourceOperator)
            || e.Data.Contains(KnownLibraryItemFormats.FilterEffect)
            || e.Data.Contains(KnownLibraryItemFormats.Transform)
            || (e.Data.GetFiles()?.Any() ?? false))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
