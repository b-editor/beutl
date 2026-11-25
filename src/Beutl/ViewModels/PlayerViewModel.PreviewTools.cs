namespace Beutl.ViewModels;

internal enum PreviewTool
{
    Move,
    Hand,
    Crop,
    Camera3D
}

public partial class PlayerViewModel
{
    private PreviewTool? _toolAfterTemporaryHand;

    internal void SelectPreviewTool(PreviewTool tool)
    {
        if (_toolAfterTemporaryHand.HasValue)
        {
            // A selection made while panning takes effect at the end of the gesture.
            _toolAfterTemporaryHand = tool;
            return;
        }

        ApplyPreviewTool(tool);
    }

    internal void BeginTemporaryHand()
    {
        _toolAfterTemporaryHand ??= (IsHandMode.Value, IsCropMode.Value, IsCameraMode.Value) switch
        {
            (true, _, _) => PreviewTool.Hand,
            (_, true, _) => PreviewTool.Crop,
            (_, _, true) => PreviewTool.Camera3D,
            _ => PreviewTool.Move
        };
        ApplyPreviewTool(PreviewTool.Hand);
    }

    internal void EndTemporaryHand()
    {
        if (_toolAfterTemporaryHand is not { } tool) return;

        _toolAfterTemporaryHand = null;
        ApplyPreviewTool(tool);
    }

    private void ApplyPreviewTool(PreviewTool tool)
    {
        IsMoveMode.Value = tool == PreviewTool.Move;
        IsHandMode.Value = tool == PreviewTool.Hand;
        IsCropMode.Value = tool == PreviewTool.Crop;
        IsCameraMode.Value = tool == PreviewTool.Camera3D;
    }
}
