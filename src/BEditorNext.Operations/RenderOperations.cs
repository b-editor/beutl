using BEditorNext.Media;
using BEditorNext.Operations.BitmapEffect;
using BEditorNext.Operations.Transform;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public static class RenderOperations
{
    public static void RegisterAll()
    {
        RenderOperationRegistry.RegisterOperations("EffectString", Colors.Teal)
            .Add<BinarizationOperation>("BinarizationString")
            .Add<BrightnessOperation>("BrightnessString")
            .Add<ChromaKeyOperation>("ChromaKeyString")
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
        RenderOperationRegistry.RegisterOperation<FormattedTextOperation>("TextString");
        RenderOperationRegistry.RegisterOperation<TestOperation>("TestString");
    }
}
