namespace BeUtl.Language;

#pragma warning disable IDE0002

public static partial class StringResources
{
    public static class OpName
    {
        //S.OpName.Ellipse
        public static string Ellipse => "S.OpName.Ellipse".GetStringResource("Ellipse");
        //S.OpName.Rect
        public static string Rect => "S.OpName.Rect".GetStringResource("Rect");
        //S.OpName.Text
        public static string Text => StringResources.Common.Text;
        //S.OpName.ImageFile
        public static string ImageFile => "S.OpName.ImageFile".GetStringResource("Image File");
        //S.OpName.Align
        public static string Align => "S.OpName.Align".GetStringResource("Align");
        //S.OpName.Blend
        public static string Blend => "S.OpName.Blend".GetStringResource("Blend");
        //S.OpName.OffscreenDrawing
        public static string OffscreenDrawing => "S.OpName.OffscreenDrawing".GetStringResource("Offscreen drawing");
        //S.OpName.RoundedRect
        public static string RoundedRect => "S.OpName.RoundedRect".GetStringResource("Rounded rect");
        //S.OpName.Test
        public static string Test => "S.OpName.Test".GetStringResource("Test");
        //S.OpName.Transform
        public static string Transform => "S.OpName.Transform".GetStringResource("Transform");
        //S.OpName.Rotate
        public static string Rotate => "S.OpName.Rotate".GetStringResource("Rotate");
        //S.OpName.Rotate3D
        public static string Rotate3D => "S.OpName.Rotate3D".GetStringResource("Rotate3D");
        //S.OpName.Scale
        public static string Scale => "S.OpName.Scale".GetStringResource("Scale");
        //S.OpName.Skew
        public static string Skew => "S.OpName.Skew".GetStringResource("Skew");
        //S.OpName.Translate
        public static string Translate => "S.OpName.Translate".GetStringResource("Translate");
        //S.OpName.Effect
        public static string Effect => "S.OpName.Effect".GetStringResource("Effect");
        //S.OpName.Blur
        public static string Blur => "S.OpName.Blur".GetStringResource("Blur");
        //S.OpName.DropShadow
        public static string DropShadow => "S.OpName.DropShadow".GetStringResource("DropShadow");

        //S.OpName.Ellipse
        private static IObservable<string>? s_ellipse;
        public static IObservable<string> EllipseObservable => s_ellipse ??= "S.OpName.Ellipse".GetStringObservable("Ellipse");
        //S.OpName.Rect
        private static IObservable<string>? s_rect;
        public static IObservable<string> RectObservable => s_rect ??= "S.OpName.Rect".GetStringObservable("Rect");
        //S.OpName.Text
        public static IObservable<string> TextObservable => StringResources.Common.TextObservable;
        //S.OpName.ImageFile
        private static IObservable<string>? s_imageFile;
        public static IObservable<string> ImageFileObservable => s_imageFile ??= "S.OpName.ImageFile".GetStringObservable("Image File");
        //S.OpName.Align
        private static IObservable<string>? s_align;
        public static IObservable<string> AlignObservable => s_align ??= "S.OpName.Align".GetStringObservable("Align");
        //S.OpName.Blend
        private static IObservable<string>? s_blend;
        public static IObservable<string> BlendObservable => s_blend ??= "S.OpName.Blend".GetStringObservable("Blend");
        //S.OpName.OffscreenDrawing
        private static IObservable<string>? s_offscreenDrawing;
        public static IObservable<string> OffscreenDrawingObservable => s_offscreenDrawing ??= "S.OpName.OffscreenDrawing".GetStringObservable("Offscreen drawing");
        //S.OpName.RoundedRect
        private static IObservable<string>? s_roundedRect;
        public static IObservable<string> RoundedRectObservable => s_roundedRect ??= "S.OpName.RoundedRect".GetStringObservable("Rounded rect");
        //S.OpName.Test
        private static IObservable<string>? s_test;
        public static IObservable<string> TestObservable => s_test ??= "S.OpName.Test".GetStringObservable("Test");
        //S.OpName.Transform
        private static IObservable<string>? s_transform;
        public static IObservable<string> TransformObservable => s_transform ??= "S.OpName.Transform".GetStringObservable("Transform");
        //S.OpName.Rotate
        private static IObservable<string>? s_rotate;
        public static IObservable<string> RotateObservable => s_rotate ??= "S.OpName.Rotate".GetStringObservable("Rotate");
        //S.OpName.Rotate3D
        private static IObservable<string>? s_rotate3D;
        public static IObservable<string> Rotate3DObservable => s_rotate3D ??= "S.OpName.Rotate3D".GetStringObservable("Rotate3D");
        //S.OpName.Scale
        private static IObservable<string>? s_scale;
        public static IObservable<string> ScaleObservable => s_scale ??= "S.OpName.Scale".GetStringObservable("Scale");
        //S.OpName.Skew
        private static IObservable<string>? s_skew;
        public static IObservable<string> SkewObservable => s_skew ??= "S.OpName.Skew".GetStringObservable("Skew");
        //S.OpName.Translate
        private static IObservable<string>? s_translate;
        public static IObservable<string> TranslateObservable => s_translate ??= "S.OpName.Translate".GetStringObservable("Translate");
        //S.OpName.Effect
        private static IObservable<string>? s_effect;
        public static IObservable<string> EffectObservable => s_effect ??= "S.OpName.Effect".GetStringObservable("Effect");
        //S.OpName.Blur
        private static IObservable<string>? s_blur;
        public static IObservable<string> BlurObservable => s_blur ??= "S.OpName.Blur".GetStringObservable("Blur");
        //S.OpName.DropShadow
        private static IObservable<string>? s_dropShadow;
        public static IObservable<string> DropShadowObservable => s_dropShadow ??= "S.OpName.DropShadow".GetStringObservable("DropShadow");
    }
}
