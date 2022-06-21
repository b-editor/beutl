using BeUtl.Media;
using BeUtl.Operations.Filters;
using BeUtl.Operations.Shapes;
using BeUtl.Operations.Transform;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public static class RenderOperations
{
    public static void RegisterAll()
    {
        LayerOperationRegistry.RegisterOperations("S.OpName.Effect", Colors.Teal)
            .Add<BlurOperation>("S.OpName.Blur")
            .Add<DropShadowOperation>("S.OpName.DropShadow")
            .Register();

        LayerOperationRegistry.RegisterOperations("S.OpName.Transform", Colors.Teal)
            .Add<RotateTransform>("S.OpName.Rotate")
            .Add<Rotate3DTransform>("S.OpName.Rotate3D")
            .Add<ScaleTransform>("S.OpName.Scale")
            .Add<SkewTransform>("S.OpName.Skew")
            .Add<TranslateTransform>("S.OpName.Translate")
            .Add<AlignOperation>("S.OpName.Align")
            .Register();

        LayerOperationRegistry.RegisterOperation<EllipseOperation>("S.OpName.Ellipse");
        LayerOperationRegistry.RegisterOperation<RectOperation>("S.OpName.Rect");
        LayerOperationRegistry.RegisterOperation<RoundedRectOperation>("S.OpName.RoundedRect");
        LayerOperationRegistry.RegisterOperation<FormattedTextOperation>("S.OpName.Text");
        LayerOperationRegistry.RegisterOperation<ImageFileOperation>("S.OpName.ImageFile");
        LayerOperationRegistry.RegisterOperation<BlendOperation>("S.OpName.Blend");
        LayerOperationRegistry.RegisterOperation<OffscreenDrawing>("S.OpName.OffscreenDrawing");
        LayerOperationRegistry.RegisterOperation<TestOperation>("S.OpName.Test");
        LayerOperationRegistry.RegisterOperation<Effects.BlurOperation>("S.OpName.Blur");
        LayerOperationRegistry.RegisterOperation<Effects.InnerShadowOperation>("S.OpName.InnerShadow");
    }
}
