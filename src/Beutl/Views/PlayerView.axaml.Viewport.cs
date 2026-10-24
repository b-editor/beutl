using Beutl.ViewModels;

namespace Beutl.Views;

public partial class PlayerView
{
    private PlayerViewportTransform? _viewportTransform;

    internal PlayerViewportTransform? ViewportTransform
    {
        get
        {
            if (DataContext is not PlayerViewModel { Scene: { } scene } || image is null)
                return _viewportTransform = null;

            if (_viewportTransform == null || _viewportTransform.PreviewSize != image.Bounds.Size || _viewportTransform.FrameSize != scene.FrameSize.Value)
                _viewportTransform = PlayerViewportTransform.Create(image.Bounds.Size, scene.FrameSize.Value);
            return _viewportTransform;
        }
    }

    private void UpdateViewportScale()
    {
        pathEditorView.ViewportScale = ViewportTransform?.FrameScale ?? 1;
        UpdateTransformHandles();
    }
}
