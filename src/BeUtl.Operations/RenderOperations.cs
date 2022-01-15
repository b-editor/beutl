using BeUtl.Media;
using BeUtl.Operations.Filters;
using BeUtl.Operations.Transform;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public static class RenderOperations
{
    public static void RegisterAll()
    {
        RenderOperationRegistry.RegisterOperations("EffectString", Colors.Teal)
            .Add<BlurOperation>("BlurString")
            .Add<DropShadowOperation>("DropShadowString")
            .Register();

        RenderOperationRegistry.RegisterOperations("TransformString", Colors.Teal)
            .Add<RotateTransform>("RotateString")
            .Add<ScaleTransform>("ScaleString")
            .Add<SkewTransform>("SkewString")
            .Add<TranslateTransform>("TranslateString")
            .Add<AlignOperation>("AlignString")
            .Register();

        RenderOperationRegistry.RegisterOperation<EllipseOperation>("EllipseString");
        RenderOperationRegistry.RegisterOperation<RectOperation>("RectString");
        RenderOperationRegistry.RegisterOperation<RoundedRectOperation>("RoundedRectString");
        RenderOperationRegistry.RegisterOperation<FormattedTextOperation>("TextString");
        RenderOperationRegistry.RegisterOperation<ImageFileOperation>("ImageFileString");
        RenderOperationRegistry.RegisterOperation<BlendOperation>("BlendString");
        RenderOperationRegistry.RegisterOperation<OffscreenDrawing>("OffscreenDrawingString");
        RenderOperationRegistry.RegisterOperation<RenderAllOperation>("RenderAllString");
        RenderOperationRegistry.RegisterOperation<TestOperation>("TestString");
    }
}
