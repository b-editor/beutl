using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeTree.Nodes;
using Beutl.NodeTree.Rendering;
using Vector = Beutl.Graphics.Vector;

namespace Beutl.NodeTree;

public static class ItemValueHelper
{
    public static void RegisterDefaultReceiver<T>(ItemValue<T> itemValue, IModifiableHierarchical hierarchical)
    {
        switch (itemValue)
        {
            case ItemValue<Drawable?> ds:
                ds.AcceptNode(hierarchical);
                break;
            case ItemValue<Transform?> ts:
                ts.AcceptMatrix(hierarchical);
                break;
            case ItemValue<Thickness> thicknessSocket:
                thicknessSocket.AcceptNumber();
                break;
            case ItemValue<Vector> vectorSocket:
                vectorSocket.AcceptNumber();
                break;
            case ItemValue<Point> pointSocket:
                pointSocket.AcceptNumber();
                break;
            case ItemValue<Size> sizeSocket:
                sizeSocket.AcceptNumber();
                break;
            case ItemValue<Rect> rectSocket:
                rectSocket.AcceptNumber();
                break;
            case ItemValue<PixelPoint> pixelPointSocket:
                pixelPointSocket.AcceptNumber();
                break;
            case ItemValue<PixelSize> pixelSizeSocket:
                pixelSizeSocket.AcceptNumber();
                break;
            case ItemValue<PixelRect> pixelRectSocket:
                pixelRectSocket.AcceptNumber();
                break;
            case ItemValue<float> floatSocket:
                floatSocket.AcceptNumber();
                break;
            case ItemValue<double> doubleSocket:
                doubleSocket.AcceptNumber();
                break;
            case ItemValue<int> intSocket:
                intSocket.AcceptNumber();
                break;
            case ItemValue<long> longSocket:
                longSocket.AcceptNumber();
                break;
            case ItemValue<short> shortSocket:
                shortSocket.AcceptNumber();
                break;
            case ItemValue<byte> byteSocket:
                byteSocket.AcceptNumber();
                break;
            case ItemValue<sbyte> sbyteSocket:
                sbyteSocket.AcceptNumber();
                break;
            case ItemValue<uint> uintSocket:
                uintSocket.AcceptNumber();
                break;
            case ItemValue<ulong> ulongSocket:
                ulongSocket.AcceptNumber();
                break;
            case ItemValue<ushort> ushortSocket:
                ushortSocket.AcceptNumber();
                break;
            case ItemValue<nint> nintSocket:
                nintSocket.AcceptNumber();
                break;
            case ItemValue<nuint> nuintSocket:
                nuintSocket.AcceptNumber();
                break;
            case ItemValue<Half> halfSocket:
                halfSocket.AcceptNumber();
                break;
            case ItemValue<decimal> decimalSocket:
                decimalSocket.AcceptNumber();
                break;
            case ItemValue<Int128> int128Socket:
                int128Socket.AcceptNumber();
                break;
            case ItemValue<UInt128> uint128Socket:
                uint128Socket.AcceptNumber();
                break;
        }
    }

    public static void AcceptNode(this ItemValue<Drawable?> itemValue, IModifiableHierarchical hierarchical)
    {
        // RenderNodeを受け取った時RenderNodeDrawableに変換する
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = null;
            var obj = source.GetBoxed();
            if (obj == null)
            {
                if (itemValue.Value is RenderNodeDrawable renderNodeDrawable)
                {
                    hierarchical.RemoveChild(renderNodeDrawable);
                }

                return true;
            }

            if (obj is Drawable drawable)
            {
                if (itemValue.Value is RenderNodeDrawable renderNodeDrawable)
                {
                    hierarchical.RemoveChild(renderNodeDrawable);
                }

                value = drawable;
                return true;
            }

            if (obj is RenderNode node)
            {
                if (itemValue.Value is not RenderNodeDrawable renderNodeDrawable)
                {
                    itemValue.Value = renderNodeDrawable = new RenderNodeDrawable();
                    hierarchical.AddChild(renderNodeDrawable);
                }

                renderNodeDrawable.Node = node;

                return true;
            }

            return false;
        });
        itemValue.RegisterDisposer(() =>
        {
            if (itemValue.Value is RenderNodeDrawable renderNodeDrawable)
            {
                hierarchical.RemoveChild(renderNodeDrawable);
            }
        });
    }

    public static void AcceptMatrix(this ItemValue<Transform?> itemValue, IModifiableHierarchical hierarchical)
    {
        // Matrixを受け取った時MatrixTransformに変換する
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = null;
            var obj = source.GetBoxed();
            if (obj == null)
            {
                if (itemValue.Value is MatrixTransform matrixTransform)
                {
                    hierarchical.RemoveChild(matrixTransform);
                }

                return true;
            }

            if (obj is Transform transform)
            {
                if (itemValue.Value is MatrixTransform matrixTransform &&
                    !ReferenceEquals(transform, matrixTransform))
                {
                    hierarchical.RemoveChild(matrixTransform);
                }

                value = transform;
                return true;
            }

            if (obj is Matrix matrix)
            {
                if (itemValue.Value is not MatrixTransform matrixTransform)
                {
                    itemValue.Value = matrixTransform = new MatrixTransform();
                    hierarchical.AddChild(matrixTransform);
                }

                matrixTransform.Matrix.CurrentValue = matrix;

                return true;
            }

            return false;
        });
        itemValue.RegisterDisposer(() =>
        {
            if (itemValue.Value is MatrixTransform matrixTransform)
            {
                hierarchical.RemoveChild(matrixTransform);
            }
        });
    }

    public static void AcceptNumber<T>(this ItemValue<T> itemValue)
        where T : INumber<T>
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber<T>(source, out T? numValue))
            {
                value = numValue;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<Thickness> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out float numValue))
            {
                value = new Thickness(numValue, numValue);
                return true;
            }

            if (source is ItemValue<string> str
                && Thickness.TryParse(str.Value, out Thickness parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<Vector> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out float numValue))
            {
                value = new Vector(numValue, numValue);
                return true;
            }

            if (source is ItemValue<Size> size)
            {
                value = new Vector(size.Value.Width, size.Value.Height);
                return true;
            }

            if (source is ItemValue<Point> point)
            {
                value = new Vector(point.Value.X, point.Value.Y);
                return true;
            }

            if (source is ItemValue<string> str
                && Vector.TryParse(str.Value, out Vector parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<Point> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out float numValue))
            {
                value = new Point(numValue, numValue);
                return true;
            }

            if (source is ItemValue<Size> size)
            {
                value = new Point(size.Value.Width, size.Value.Height);
                return true;
            }

            if (source is ItemValue<Vector> vec)
            {
                value = new Point(vec.Value.X, vec.Value.Y);
                return true;
            }

            if (source is ItemValue<string> str
                && Point.TryParse(str.Value, out Point parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<Size> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out float numValue))
            {
                value = new Size(numValue, numValue);
                return true;
            }

            if (source is ItemValue<Point> point)
            {
                value = new Size(point.Value.X, point.Value.Y);
                return true;
            }

            if (source is ItemValue<Vector> vec)
            {
                value = new Size(vec.Value.X, vec.Value.Y);
                return true;
            }

            if (source is ItemValue<string> str
                && Size.TryParse(str.Value, out Size parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<Rect> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out float numValue))
            {
                value = new Rect(numValue, numValue, numValue, numValue);
                return true;
            }

            if (source is ItemValue<Size> size)
            {
                value = new Rect(size.Value);
                return true;
            }

            if (source is ItemValue<string> str
                && Rect.TryParse(str.Value, out Rect parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<PixelPoint> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out int numValue))
            {
                value = new PixelPoint(numValue, numValue);
                return true;
            }

            if (source is ItemValue<PixelSize> size)
            {
                value = new PixelPoint(size.Value.Width, size.Value.Height);
                return true;
            }

            if (source is ItemValue<string> str
                && PixelPoint.TryParse(str.Value, out PixelPoint parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<PixelSize> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out int numValue))
            {
                value = new PixelSize(numValue, numValue);
                return true;
            }

            if (source is ItemValue<PixelPoint> point)
            {
                value = new PixelSize(point.Value.X, point.Value.Y);
                return true;
            }

            if (source is ItemValue<string> str
                && PixelSize.TryParse(str.Value, out PixelSize parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    public static void AcceptNumber(this ItemValue<PixelRect> itemValue)
    {
        itemValue.RegisterReceiver((source, out value) =>
        {
            value = default;

            if (ToNumber(source, out int numValue))
            {
                value = new PixelRect(numValue, numValue, numValue, numValue);
                return true;
            }

            if (source is ItemValue<PixelSize> size)
            {
                value = new PixelRect(size.Value);
                return true;
            }

            if (source is ItemValue<string> str
                && PixelRect.TryParse(str.Value, out PixelRect parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });
    }

    private static bool ToNumber<T>(IItemValue source, out T value)
        where T : INumber<T>
    {
        try
        {
            if (source is ItemValue<double> @double)
            {
                value = T.CreateTruncating(@double.Value);
            }
            else if (source is ItemValue<Half> half)
            {
                value = T.CreateTruncating(half.Value);
            }
            else if (source is ItemValue<short> @short)
            {
                value = T.CreateTruncating(@short.Value);
            }
            else if (source is ItemValue<long> @long)
            {
                value = T.CreateTruncating(@long.Value);
            }
            else if (source is ItemValue<Int128> int128)
            {
                value = T.CreateTruncating(int128.Value);
            }
            else if (source is ItemValue<nint> nint)
            {
                value = T.CreateTruncating(nint.Value);
            }
            else if (source is ItemValue<sbyte> @sbyte)
            {
                value = T.CreateTruncating(@sbyte.Value);
            }
            else if (source is ItemValue<float> @float)
            {
                value = T.CreateTruncating(@float.Value);
            }
            else if (source is ItemValue<char> @char)
            {
                value = T.CreateTruncating(@char.Value);
            }
            else if (source is ItemValue<decimal> @decimal)
            {
                value = T.CreateTruncating(@decimal.Value);
            }
            else if (source is ItemValue<ushort> @ushort)
            {
                value = T.CreateTruncating(@ushort.Value);
            }
            else if (source is ItemValue<uint> @uint)
            {
                value = T.CreateTruncating(@uint.Value);
            }
            else if (source is ItemValue<ulong> @ulong)
            {
                value = T.CreateTruncating(@ulong.Value);
            }
            else if (source is ItemValue<UInt128> uInt128)
            {
                value = T.CreateTruncating(uInt128.Value);
            }
            else if (source is ItemValue<nuint> nuint)
            {
                value = T.CreateTruncating(nuint.Value);
            }
            else if (source is ItemValue<byte> @byte)
            {
                value = T.CreateTruncating(@byte.Value);
            }
            else if (source is ItemValue<int> @int)
            {
                value = T.CreateTruncating(@int.Value);
            }
            else
            {
                value = default!;
                return false;
            }

            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }
}
