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
        RenderOperationRegistry.RegisterOperations("S.OpName.Effect", Colors.Teal)
            .Add<BlurOperation>("S.OpName.Blur")
            .Add<DropShadowOperation>("S.OpName.DropShadow")
            .Register();

        RenderOperationRegistry.RegisterOperations("S.OpName.Transform", Colors.Teal)
            .Add<RotateTransform>("S.OpName.Rotate")
            .Add<Rotate3DTransform>("S.OpName.Rotate3D")
            .Add<ScaleTransform>("S.OpName.Scale")
            .Add<SkewTransform>("S.OpName.Skew")
            .Add<TranslateTransform>("S.OpName.Translate")
            .Add<AlignOperation>("S.OpName.Align")
            .Register();

        RenderOperationRegistry.RegisterOperation<EllipseOperation>("S.OpName.Ellipse");
        RenderOperationRegistry.RegisterOperation<RectOperation>("S.OpName.Rect");
        RenderOperationRegistry.RegisterOperation<RoundedRectOperation>("S.OpName.RoundedRect");
        RenderOperationRegistry.RegisterOperation<FormattedTextOperation>("S.OpName.Text");
        RenderOperationRegistry.RegisterOperation<ImageFileOperation>("S.OpName.ImageFile");
        RenderOperationRegistry.RegisterOperation<BlendOperation>("S.OpName.Blend");
        RenderOperationRegistry.RegisterOperation<OffscreenDrawing>("S.OpName.OffscreenDrawing");
        RenderOperationRegistry.RegisterOperation<RenderAllOperation>("S.OpName.RenderAll");
        RenderOperationRegistry.RegisterOperation<TestOperation>("S.OpName.Test");
    }
}
