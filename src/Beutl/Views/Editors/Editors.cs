using Avalonia.Controls;
using Avalonia.Data;

namespace Beutl.Views.Editors
{
    // Vector2
    public sealed class PixelPointEditor : BaseVector2Editor<Media.PixelPoint>
    {
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public PixelPointEditor()
        {
            xText.Text = "X";
            yText.Text = "Y";

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override Media.PixelPoint IncrementX(Media.PixelPoint value, int increment)
        {
            return new Media.PixelPoint(
                value.X + increment,
                value.Y);
        }

        protected override Media.PixelPoint IncrementY(Media.PixelPoint value, int increment)
        {
            return new Media.PixelPoint(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out Media.PixelPoint value)
        {
            if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
            {
                value = new Media.PixelPoint(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class PixelSizeEditor : BaseVector2Editor<Media.PixelSize>
    {
        private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

        public PixelSizeEditor()
        {
            xText.Text = Strings.Width;
            yText.Text = Strings.Height;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override Media.PixelSize IncrementX(Media.PixelSize value, int increment)
        {
            return new Media.PixelSize(
                value.Width + increment,
                value.Height);
        }

        protected override Media.PixelSize IncrementY(Media.PixelSize value, int increment)
        {
            return new Media.PixelSize(
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, out Media.PixelSize value)
        {
            if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
            {
                value = new Media.PixelSize(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class PointEditor : BaseVector2Editor<Graphics.Point>
    {
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public PointEditor()
        {
            xText.Text = "X";
            yText.Text = "Y";

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override Graphics.Point IncrementX(Graphics.Point value, int increment)
        {
            return new Graphics.Point(
                value.X + increment,
                value.Y);
        }

        protected override Graphics.Point IncrementY(Graphics.Point value, int increment)
        {
            return new Graphics.Point(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out Graphics.Point value)
        {
            if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
            {
                value = new Graphics.Point(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class SizeEditor : BaseVector2Editor<Graphics.Size>
    {
        private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

        public SizeEditor()
        {
            xText.Text = Strings.Width;
            yText.Text = Strings.Height;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override Graphics.Size IncrementX(Graphics.Size value, int increment)
        {
            return new Graphics.Size(
                value.Width + increment,
                value.Height);
        }

        protected override Graphics.Size IncrementY(Graphics.Size value, int increment)
        {
            return new Graphics.Size(
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, out Graphics.Size value)
        {
            if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
            {
                value = new Graphics.Size(xi, yi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class VectorEditor : BaseVector2Editor<Graphics.Vector>
    {
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public VectorEditor()
        {
            xText.Text = "X";
            yText.Text = "Y";

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
        }

        protected override Graphics.Vector IncrementX(Graphics.Vector value, int increment)
        {
            return new Graphics.Vector(
                value.X + increment,
                value.Y);
        }

        protected override Graphics.Vector IncrementY(Graphics.Vector value, int increment)
        {
            return new Graphics.Vector(
                value.X,
                value.Y + increment);
        }

        protected override bool TryParse(string? x, string? y, out Graphics.Vector value)
        {
            if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
            {
                value = new Graphics.Vector(xi, yi);
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
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

        public Vector2Editor()
        {
            xText.Text = "X";
            yText.Text = "Y";

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
            if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
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
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);

        public Vector3Editor()
        {
            xText.Text = "X";
            yText.Text = "Y";
            zText.Text = "Z";

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
            if (float.TryParse(x, out float xi)
                && float.TryParse(y, out float yi)
                && float.TryParse(z, out float zi))
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
    public sealed class PixelRectEditor : BaseVector4Editor<Media.PixelRect>
    {
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

        public PixelRectEditor()
        {
            xText.Text = "X";
            yText.Text = "Y";
            zText.Text = Strings.Width;
            wText.Text = Strings.Height;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
            wTextBox[!TextBox.TextProperty] = s_w;
        }

        protected override Media.PixelRect IncrementX(Media.PixelRect value, int increment)
        {
            return new Media.PixelRect(
                value.X + increment,
                value.Y,
                value.Width,
                value.Height);
        }

        protected override Media.PixelRect IncrementY(Media.PixelRect value, int increment)
        {
            return new Media.PixelRect(
                value.X,
                value.Y + increment,
                value.Width,
                value.Height);
        }

        protected override Media.PixelRect IncrementZ(Media.PixelRect value, int increment)
        {
            return new Media.PixelRect(
                value.X,
                value.Y,
                value.Width + increment,
                value.Height);
        }

        protected override Media.PixelRect IncrementW(Media.PixelRect value, int increment)
        {
            return new Media.PixelRect(
                value.X,
                value.Y,
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, string? w, out Media.PixelRect value)
        {
            if (int.TryParse(x, out int xi)
                && int.TryParse(y, out int yi)
                && int.TryParse(z, out int zi)
                && int.TryParse(w, out int wi))
            {
                value = new Media.PixelRect(xi, yi, zi, wi);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
    }
    public sealed class RectEditor : BaseVector4Editor<Graphics.Rect>
    {
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

        public RectEditor()
        {
            xText.Text = "X";
            yText.Text = "Y";
            zText.Text = Strings.Width;
            wText.Text = Strings.Height;

            xTextBox[!TextBox.TextProperty] = s_x;
            yTextBox[!TextBox.TextProperty] = s_y;
            zTextBox[!TextBox.TextProperty] = s_z;
            wTextBox[!TextBox.TextProperty] = s_w;
        }

        protected override Graphics.Rect IncrementX(Graphics.Rect value, int increment)
        {
            return new Graphics.Rect(
                value.X + increment,
                value.Y,
                value.Width,
                value.Height);
        }

        protected override Graphics.Rect IncrementY(Graphics.Rect value, int increment)
        {
            return new Graphics.Rect(
                value.X,
                value.Y + increment,
                value.Width,
                value.Height);
        }

        protected override Graphics.Rect IncrementZ(Graphics.Rect value, int increment)
        {
            return new Graphics.Rect(
                value.X,
                value.Y,
                value.Width + increment,
                value.Height);
        }

        protected override Graphics.Rect IncrementW(Graphics.Rect value, int increment)
        {
            return new Graphics.Rect(
                value.X,
                value.Y,
                value.Width,
                value.Height + increment);
        }

        protected override bool TryParse(string? x, string? y, string? z, string? w, out Graphics.Rect value)
        {
            if (float.TryParse(x, out float xi)
                && float.TryParse(y, out float yi)
                && float.TryParse(z, out float zi)
                && float.TryParse(w, out float wi))
            {
                value = new Graphics.Rect(xi, yi, zi, wi);
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
        private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
        private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
        private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);
        private static readonly Binding s_w = new("Value.Value.W", BindingMode.OneWay);

        public Vector4Editor()
        {
            xText.Text = "X";
            yText.Text = "Y";
            zText.Text = "Z";
            wText.Text = "W";

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
            if (float.TryParse(x, out float xi)
                && float.TryParse(y, out float yi)
                && float.TryParse(z, out float zi)
                && float.TryParse(w, out float wi))
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
