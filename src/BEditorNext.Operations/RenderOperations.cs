using BEditorNext.Media;
using BEditorNext.Operations.Transform;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public static class RenderOperations
{
    public static void RegisterAll()
    {
        RenderOperationRegistry.RegisterOperations("TransformString", Colors.Teal)
            .Add<RotateTransform>("RotateString")
            .Add<ScaleTransform>("ScaleString")
            .Add<SkewTransform>("SkewString")
            .Add<TranslateTransform>("TranslateString")
            .Add<AlignOperation>("AlignString")
            .Register();

        RenderOperationRegistry.RegisterOperation<EllipseOperation>("EllipseString");
        RenderOperationRegistry.RegisterOperation<TestOperation>("TestString");
    }
}
