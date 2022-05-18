namespace BeUtl.Language;

#pragma warning disable IDE0002

public static partial class StringResources
{
    public static class Editors
    {
        public static class AnimationEditors
        {
            //S.Editors.AnimationEditor.Remove
            public static string Remove => StringResources.Common.Remove;
            //S.Editors.AnimationEditor.Edit
            public static string Edit => StringResources.Common.Edit;
            //S.Editors.AnimationEditor.Close
            public static string Close => StringResources.Common.Close;

            //S.Editors.AnimationEditor.Remove
            public static IObservable<string> RemoveObservable => StringResources.Common.RemoveObservable;
            //S.Editors.AnimationEditor.Edit
            public static IObservable<string> EditObservable => StringResources.Common.EditObservable;
            //S.Editors.AnimationEditor.Close
            public static IObservable<string> CloseObservable => StringResources.Common.CloseObservable;
        }

        public static class Badge
        {
            //S.Editors.Badge.Reset
            public static string Reset => StringResources.Common.Reset;
            //S.Editors.Badge.EditAnimation
            public static string EditAnimation => StringResources.Common.EditAnimation;

            //S.Editors.Badge.Reset
            public static IObservable<string> ResetObservable => StringResources.Common.ResetObservable;
            //S.Editors.Badge.EditAnimation
            public static IObservable<string> EditAnimationObservable => StringResources.Common.EditAnimationObservable;
        }

        public static class Operation
        {
            //S.Editors.Operation.Remove
            public static string Remove => StringResources.Common.Remove;

            //S.Editors.Operation.Remove
            public static IObservable<string> RemoveObservable => StringResources.Common.RemoveObservable;
        }

        public static class PixelPoint
        {
            //S.Editors.PixelPoint.X
            public static string X => StringResources.Common.X;
            //S.Editors.PixelPoint.Y
            public static string Y => StringResources.Common.Y;

            //S.Editors.PixelPoint.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.PixelPoint.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
        }

        public static class PixelRect
        {
            //S.Editors.PixelRect.X
            public static string X => StringResources.Common.X;
            //S.Editors.PixelRect.Y
            public static string Y => StringResources.Common.Y;
            //S.Editors.PixelRect.Z
            public static string Z => StringResources.Common.Width;
            //S.Editors.PixelRect.W
            public static string W => StringResources.Common.Height;

            //S.Editors.PixelRect.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.PixelRect.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
            //S.Editors.PixelRect.Z
            public static IObservable<string> ZObservable => StringResources.Common.WidthObservable;
            //S.Editors.PixelRect.W
            public static IObservable<string> WObservable => StringResources.Common.HeightObservable;
        }

        public static class PixelSize
        {
            //S.Editors.PixelSize.X
            public static string X => StringResources.Common.Width;
            //S.Editors.PixelSize.Y
            public static string Y => StringResources.Common.Height;

            //S.Editors.PixelSize.X
            public static IObservable<string> XObservable => StringResources.Common.WidthObservable;
            //S.Editors.PixelSize.Y
            public static IObservable<string> YObservable => StringResources.Common.HeightObservable;
        }

        public static class Point
        {
            //S.Editors.Point.X
            public static string X => StringResources.Common.X;
            //S.Editors.Point.Y
            public static string Y => StringResources.Common.Y;

            //S.Editors.Point.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Point.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
        }

        public static class Rect
        {
            //S.Editors.Rect.X
            public static string X => StringResources.Common.X;
            //S.Editors.Rect.Y
            public static string Y => StringResources.Common.Y;
            //S.Editors.Rect.Z
            public static string Z => StringResources.Common.Width;
            //S.Editors.Rect.W
            public static string W => StringResources.Common.Height;

            //S.Editors.Rect.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Rect.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
            //S.Editors.Rect.Z
            public static IObservable<string> ZObservable => StringResources.Common.WidthObservable;
            //S.Editors.Rect.W
            public static IObservable<string> WObservable => StringResources.Common.HeightObservable;
        }

        public static class Size
        {
            //S.Editors.Size.X
            public static string X => StringResources.Common.Width;
            //S.Editors.Size.Y
            public static string Y => StringResources.Common.Height;

            //S.Editors.Size.X
            public static IObservable<string> XObservable => StringResources.Common.WidthObservable;
            //S.Editors.Size.Y
            public static IObservable<string> YObservable => StringResources.Common.HeightObservable;
        }

        public static class Vector2
        {
            //S.Editors.Vector2.X
            public static string X => StringResources.Common.X;
            //S.Editors.Vector2.Y
            public static string Y => StringResources.Common.Y;

            //S.Editors.Vector2.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Vector2.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
        }

        public static class Vector3
        {
            //S.Editors.Vector3.X
            public static string X => StringResources.Common.X;
            //S.Editors.Vector3.Y
            public static string Y => StringResources.Common.Y;
            //S.Editors.Vector3.Z
            public static string Z => StringResources.Common.Z;

            //S.Editors.Vector3.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Vector3.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
            //S.Editors.Vector3.Z
            public static IObservable<string> ZObservable => StringResources.Common.ZObservable;
        }

        public static class Vector4
        {
            //S.Editors.Vector4.X
            public static string X => StringResources.Common.X;
            //S.Editors.Vector4.Y
            public static string Y => StringResources.Common.Y;
            //S.Editors.Vector4.Z
            public static string Z => StringResources.Common.Z;
            //S.Editors.Vector4.W
            public static string W => StringResources.Common.W;

            //S.Editors.Vector4.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Vector4.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
            //S.Editors.Vector4.Z
            public static IObservable<string> ZObservable => StringResources.Common.ZObservable;
            //S.Editors.Vector4.W
            public static IObservable<string> WObservable => StringResources.Common.WObservable;
        }

        public static class Vector
        {
            //S.Editors.Vector.X
            public static string X => StringResources.Common.X;
            //S.Editors.Vector.Y
            public static string Y => StringResources.Common.Y;

            //S.Editors.Vector.X
            public static IObservable<string> XObservable => StringResources.Common.XObservable;
            //S.Editors.Vector.Y
            public static IObservable<string> YObservable => StringResources.Common.YObservable;
        }
    }
}
