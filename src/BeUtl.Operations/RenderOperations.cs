using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public static class RenderOperations
{
    public static void RegisterAll()
    {
        LayerOperationRegistry.RegisterOperations("S.Opearations.Source")
            .Add<Source.EllipseOperation>("S.Opearations.Source.Ellipse")
            .Add<Source.RectOperation>("S.Opearations.Source.Rect")
            .Add<Source.RoundedRectOperation>("S.Opearations.Source.RoundedRect")
            .Add<Source.FormattedTextOperation>("S.Opearations.Source.Text")
            .Add<Source.ImageFileOperation>("S.Opearations.Source.ImageFile")
            .Register();

        LayerOperationRegistry.RegisterOperations("S.Opearations.Configure")
            .Add<Configure.AlignOperation>("S.Opearations.Configure.Alignment")
            .Add<Configure.BlendOperation>("S.Opearations.Configure.Blend")
            .AddGroup("S.Opearations.Configure.ImageFilter", helper => helper
                .Add<Configure.ImageFilter.BlurOperation>("S.Opearations.Configure.ImageFilter.Blur")
                .Add<Configure.ImageFilter.DropShadowOperation>("S.Opearations.Configure.ImageFilter.DropShadow")
                .Register())
            .AddGroup("S.Opearations.Configure.BitmapEffect", helper => helper
                .Add<Configure.BitmapEffect.BlurOperation>("S.Opearations.Configure.BitmapEffect.Blur")
                .Add<Configure.BitmapEffect.InnerShadowOperation>("S.Opearations.Configure.BitmapEffect.InnerShadow")
                .Add<Configure.BitmapEffect.BorderOperation>("S.Opearations.Configure.BitmapEffect.Border")
                .Register())
            .AddGroup("S.Opearations.Configure.Transform", helper => helper
                .Add<Configure.Transform.RotateTransform>("S.Opearations.Configure.Transform.Rotation")
                .Add<Configure.Transform.Rotate3DTransform>("S.Opearations.Configure.Transform.Rotation3D")
                .Add<Configure.Transform.ScaleTransform>("S.Opearations.Configure.Transform.Scale")
                .Add<Configure.Transform.SkewTransform>("S.Opearations.Configure.Transform.Skew")
                .Add<Configure.Transform.TranslateTransform>("S.Opearations.Configure.Transform.Translate")
                .Register())
            .Register();
    }
}
