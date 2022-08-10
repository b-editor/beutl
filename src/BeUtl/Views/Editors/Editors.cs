using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

#pragma warning disable IDE0001, IDE0049

namespace BeUtl.Views.Editors
{
    // Vector2
    public sealed class PixelPointEditor : BaseVector2Editor<BeUtl.Media.PixelPoint>
    {
        private static readonly IBinding s_xResource = S.Editors.PixelPoint.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.PixelPoint.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public PixelPointEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override BeUtl.Media.PixelPoint IncrementX(BeUtl.Media.PixelPoint value, int increment)
        {
            return new BeUtl.Media.PixelPoint(
                value.X + increment,
                value.Y);
        }

        protected override BeUtl.Media.PixelPoint IncrementY(BeUtl.Media.PixelPoint value, int increment)
        {
            return new BeUtl.Media.PixelPoint(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out BeUtl.Media.PixelPoint value)
        {
            if (System.Int32.TryParse(x, out System.Int32 xi) && System.Int32.TryParse(y, out System.Int32 yi))
            {
                value = new BeUtl.Media.PixelPoint(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class PixelSizeEditor : BaseVector2Editor<BeUtl.Media.PixelSize>
    {
        private static readonly IBinding s_xResource = S.Editors.PixelSize.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.PixelSize.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

        public PixelSizeEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override BeUtl.Media.PixelSize IncrementX(BeUtl.Media.PixelSize value, int increment)
        {
            return new BeUtl.Media.PixelSize(
                value.Width + increment,
                value.Height);
        }

        protected override BeUtl.Media.PixelSize IncrementY(BeUtl.Media.PixelSize value, int increment)
        {
            return new BeUtl.Media.PixelSize(
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, out BeUtl.Media.PixelSize value)
        {
            if (System.Int32.TryParse(x, out System.Int32 xi) && System.Int32.TryParse(y, out System.Int32 yi))
            {
                value = new BeUtl.Media.PixelSize(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class PointEditor : BaseVector2Editor<BeUtl.Graphics.Point>
    {
        private static readonly IBinding s_xResource = S.Editors.Point.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Point.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public PointEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override BeUtl.Graphics.Point IncrementX(BeUtl.Graphics.Point value, int increment)
        {
            return new BeUtl.Graphics.Point(
                value.X + increment,
                value.Y);
        }

        protected override BeUtl.Graphics.Point IncrementY(BeUtl.Graphics.Point value, int increment)
        {
            return new BeUtl.Graphics.Point(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out BeUtl.Graphics.Point value)
        {
            if (System.Single.TryParse(x, out System.Single xi) && System.Single.TryParse(y, out System.Single yi))
            {
                value = new BeUtl.Graphics.Point(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class SizeEditor : BaseVector2Editor<BeUtl.Graphics.Size>
    {
        private static readonly IBinding s_xResource = S.Editors.Size.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Size.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

        public SizeEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override BeUtl.Graphics.Size IncrementX(BeUtl.Graphics.Size value, int increment)
        {
            return new BeUtl.Graphics.Size(
                value.Width + increment,
                value.Height);
        }

        protected override BeUtl.Graphics.Size IncrementY(BeUtl.Graphics.Size value, int increment)
        {
            return new BeUtl.Graphics.Size(
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, out BeUtl.Graphics.Size value)
        {
            if (System.Single.TryParse(x, out System.Single xi) && System.Single.TryParse(y, out System.Single yi))
            {
                value = new BeUtl.Graphics.Size(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class VectorEditor : BaseVector2Editor<BeUtl.Graphics.Vector>
    {
        private static readonly IBinding s_xResource = S.Editors.Vector.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Vector.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public VectorEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override BeUtl.Graphics.Vector IncrementX(BeUtl.Graphics.Vector value, int increment)
        {
            return new BeUtl.Graphics.Vector(
                value.X + increment,
                value.Y);
        }

        protected override BeUtl.Graphics.Vector IncrementY(BeUtl.Graphics.Vector value, int increment)
        {
            return new BeUtl.Graphics.Vector(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out BeUtl.Graphics.Vector value)
        {
            if (System.Single.TryParse(x, out System.Single xi) && System.Single.TryParse(y, out System.Single yi))
            {
                value = new BeUtl.Graphics.Vector(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class Vector2Editor : BaseVector2Editor<System.Numerics.Vector2>
    {
        private static readonly IBinding s_xResource = S.Editors.Vector2.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Vector2.YObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public Vector2Editor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override System.Numerics.Vector2 IncrementX(System.Numerics.Vector2 value, int increment)
        {
            return new System.Numerics.Vector2(
                value.X + increment,
                value.Y);
        }

        protected override System.Numerics.Vector2 IncrementY(System.Numerics.Vector2 value, int increment)
        {
            return new System.Numerics.Vector2(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out System.Numerics.Vector2 value)
        {
            if (System.Single.TryParse(x, out System.Single xi) && System.Single.TryParse(y, out System.Single yi))
            {
                value = new System.Numerics.Vector2(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }

    // Vector3
    public sealed class Vector3Editor : BaseVector3Editor<System.Numerics.Vector3>
    {
        private static readonly IBinding s_xResource = S.Editors.Vector3.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Vector3.YObservable.ToBinding();
        private static readonly IBinding s_zResource = S.Editors.Vector3.ZObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);

        public Vector3Editor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;
            zText[!TextBlock.TextProperty] = s_zResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
        }

        protected override System.Numerics.Vector3 IncrementX(System.Numerics.Vector3 value, int increment)
        {
            return new System.Numerics.Vector3(
                value.X + increment,
                value.Y,
                value.Z);
        }

        protected override System.Numerics.Vector3 IncrementY(System.Numerics.Vector3 value, int increment)
        {
            return new System.Numerics.Vector3(
                value.X,
                value.Y + increment,
                value.Z);
        }

        protected override System.Numerics.Vector3 IncrementZ(System.Numerics.Vector3 value, int increment)
        {
            return new System.Numerics.Vector3(
                value.X,
                value.Y,
                value.Z + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, out System.Numerics.Vector3 value)
        {
            if (System.Single.TryParse(x, out System.Single xi)
                && System.Single.TryParse(y, out System.Single yi)
                && System.Single.TryParse(z, out System.Single zi))
            {
                value = new System.Numerics.Vector3(xi, yi, zi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }

    // Vector4
    public sealed class PixelRectEditor : BaseVector4Editor<BeUtl.Media.PixelRect>
    {
        private static readonly IBinding s_xResource = S.Editors.PixelRect.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.PixelRect.YObservable.ToBinding();
        private static readonly IBinding s_zResource = S.Editors.PixelRect.ZObservable.ToBinding();
        private static readonly IBinding s_wResource = S.Editors.PixelRect.WObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

        public PixelRectEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;
            zText[!TextBlock.TextProperty] = s_zResource;
            wText[!TextBlock.TextProperty] = s_wResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
            wTextBox[!TextBox.TextProperty] = s_w;
        }

        protected override BeUtl.Media.PixelRect IncrementX(BeUtl.Media.PixelRect value, int increment)
        {
            return new BeUtl.Media.PixelRect(
                value.X + increment,
                value.Y,
                value.Width,
                value.Height);
        }

        protected override BeUtl.Media.PixelRect IncrementY(BeUtl.Media.PixelRect value, int increment)
        {
            return new BeUtl.Media.PixelRect(
                value.X,
                value.Y + increment,
                value.Width,
                value.Height);
        }

        protected override BeUtl.Media.PixelRect IncrementZ(BeUtl.Media.PixelRect value, int increment)
        {
            return new BeUtl.Media.PixelRect(
                value.X,
                value.Y,
                value.Width + increment,
                value.Height);
        }

        protected override BeUtl.Media.PixelRect IncrementW(BeUtl.Media.PixelRect value, int increment)
        {
            return new BeUtl.Media.PixelRect(
                value.X,
                value.Y,
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, string? w, out BeUtl.Media.PixelRect value)
        {
            if (System.Int32.TryParse(x, out System.Int32 xi)
                && System.Int32.TryParse(y, out System.Int32 yi)
                && System.Int32.TryParse(z, out System.Int32 zi)
                && System.Int32.TryParse(w, out System.Int32 wi))
            {
                value = new BeUtl.Media.PixelRect(xi, yi, zi, wi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class RectEditor : BaseVector4Editor<BeUtl.Graphics.Rect>
    {
        private static readonly IBinding s_xResource = S.Editors.Rect.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Rect.YObservable.ToBinding();
        private static readonly IBinding s_zResource = S.Editors.Rect.ZObservable.ToBinding();
        private static readonly IBinding s_wResource = S.Editors.Rect.WObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

        public RectEditor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;
            zText[!TextBlock.TextProperty] = s_zResource;
            wText[!TextBlock.TextProperty] = s_wResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
            wTextBox[!TextBox.TextProperty] = s_w;
        }

        protected override BeUtl.Graphics.Rect IncrementX(BeUtl.Graphics.Rect value, int increment)
        {
            return new BeUtl.Graphics.Rect(
                value.X + increment,
                value.Y,
                value.Width,
                value.Height);
        }

        protected override BeUtl.Graphics.Rect IncrementY(BeUtl.Graphics.Rect value, int increment)
        {
            return new BeUtl.Graphics.Rect(
                value.X,
                value.Y + increment,
                value.Width,
                value.Height);
        }

        protected override BeUtl.Graphics.Rect IncrementZ(BeUtl.Graphics.Rect value, int increment)
        {
            return new BeUtl.Graphics.Rect(
                value.X,
                value.Y,
                value.Width + increment,
                value.Height);
        }

        protected override BeUtl.Graphics.Rect IncrementW(BeUtl.Graphics.Rect value, int increment)
        {
            return new BeUtl.Graphics.Rect(
                value.X,
                value.Y,
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, string? w, out BeUtl.Graphics.Rect value)
        {
            if (System.Single.TryParse(x, out System.Single xi)
                && System.Single.TryParse(y, out System.Single yi)
                && System.Single.TryParse(z, out System.Single zi)
                && System.Single.TryParse(w, out System.Single wi))
            {
                value = new BeUtl.Graphics.Rect(xi, yi, zi, wi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class Vector4Editor : BaseVector4Editor<System.Numerics.Vector4>
    {
        private static readonly IBinding s_xResource = S.Editors.Vector4.XObservable.ToBinding();
        private static readonly IBinding s_yResource = S.Editors.Vector4.YObservable.ToBinding();
        private static readonly IBinding s_zResource = S.Editors.Vector4.ZObservable.ToBinding();
        private static readonly IBinding s_wResource = S.Editors.Vector4.WObservable.ToBinding();
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.W", BindingMode.OneWay);

        public Vector4Editor()
        {
            xText[!TextBlock.TextProperty] = s_xResource;
            yText[!TextBlock.TextProperty] = s_yResource;
            zText[!TextBlock.TextProperty] = s_zResource;
            wText[!TextBlock.TextProperty] = s_wResource;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
            wTextBox[!TextBox.TextProperty] = s_w;
        }

        protected override System.Numerics.Vector4 IncrementX(System.Numerics.Vector4 value, int increment)
        {
            return new System.Numerics.Vector4(
                value.X + increment,
                value.Y,
                value.Z,
                value.W);
        }

        protected override System.Numerics.Vector4 IncrementY(System.Numerics.Vector4 value, int increment)
        {
            return new System.Numerics.Vector4(
                value.X,
                value.Y + increment,
                value.Z,
                value.W);
        }

        protected override System.Numerics.Vector4 IncrementZ(System.Numerics.Vector4 value, int increment)
        {
            return new System.Numerics.Vector4(
                value.X,
                value.Y,
                value.Z + increment,
                value.W);
        }

        protected override System.Numerics.Vector4 IncrementW(System.Numerics.Vector4 value, int increment)
        {
            return new System.Numerics.Vector4(
                value.X,
                value.Y,
                value.Z,
                value.W + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, string? w, out System.Numerics.Vector4 value)
        {
            if (System.Single.TryParse(x, out System.Single xi)
                && System.Single.TryParse(y, out System.Single yi)
                && System.Single.TryParse(z, out System.Single zi)
                && System.Single.TryParse(w, out System.Single wi))
            {
                value = new System.Numerics.Vector4(xi, yi, zi, wi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
}
