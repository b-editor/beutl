using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeTree.Nodes;
using Vector = Beutl.Graphics.Vector;

namespace Beutl.NodeTree;

public static class InputSocketHelper
{
    public static void RegisterDefaultReceiver<T>(InputSocket<T> inputSocket)
    {
        switch (inputSocket)
        {
            case InputSocket<Drawable?> ds:
                ds.AcceptNode();
                break;
            case InputSocket<Transform?> ts:
                ts.AcceptMatrix();
                break;
            case InputSocket<Thickness> thicknessSocket:
                thicknessSocket.AcceptNumber();
                break;
            case InputSocket<Vector> vectorSocket:
                vectorSocket.AcceptNumber();
                break;
            case InputSocket<Point> pointSocket:
                pointSocket.AcceptNumber();
                break;
            case InputSocket<Size> sizeSocket:
                sizeSocket.AcceptNumber();
                break;
            case InputSocket<Rect> rectSocket:
                rectSocket.AcceptNumber();
                break;
            case InputSocket<PixelPoint> pixelPointSocket:
                pixelPointSocket.AcceptNumber();
                break;
            case InputSocket<PixelSize> pixelSizeSocket:
                pixelSizeSocket.AcceptNumber();
                break;
            case InputSocket<PixelRect> pixelRectSocket:
                pixelRectSocket.AcceptNumber();
                break;
            case InputSocket<float> floatSocket:
                floatSocket.AcceptNumber();
                break;
            case InputSocket<double> doubleSocket:
                doubleSocket.AcceptNumber();
                break;
            case InputSocket<int> intSocket:
                intSocket.AcceptNumber();
                break;
            case InputSocket<long> longSocket:
                longSocket.AcceptNumber();
                break;
            case InputSocket<short> shortSocket:
                shortSocket.AcceptNumber();
                break;
            case InputSocket<byte> byteSocket:
                byteSocket.AcceptNumber();
                break;
            case InputSocket<sbyte> sbyteSocket:
                sbyteSocket.AcceptNumber();
                break;
            case InputSocket<uint> uintSocket:
                uintSocket.AcceptNumber();
                break;
            case InputSocket<ulong> ulongSocket:
                ulongSocket.AcceptNumber();
                break;
            case InputSocket<ushort> ushortSocket:
                ushortSocket.AcceptNumber();
                break;
            case InputSocket<nint> nintSocket:
                nintSocket.AcceptNumber();
                break;
            case InputSocket<nuint> nuintSocket:
                nuintSocket.AcceptNumber();
                break;
            case InputSocket<Half> halfSocket:
                halfSocket.AcceptNumber();
                break;
            case InputSocket<decimal> decimalSocket:
                decimalSocket.AcceptNumber();
                break;
            case InputSocket<Int128> int128Socket:
                int128Socket.AcceptNumber();
                break;
            case InputSocket<UInt128> uint128Socket:
                uint128Socket.AcceptNumber();
                break;
        }
    }

    public static InputSocket<Drawable?> AcceptNode(this InputSocket<Drawable?> inputSocket)
    {
        // RenderNodeを受け取った時RenderNodeDrawableに変換する
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = null;
            if (obj == null)
            {
                return true;
            }

            if (obj is Drawable drawable)
            {
                if (inputSocket.Value is RenderNodeDrawable renderNodeDrawable)
                {
                    ((IModifiableHierarchical)inputSocket).RemoveChild(renderNodeDrawable);
                }

                value = drawable;
                return true;
            }

            if (obj is RenderNode node)
            {
                if (inputSocket.Value is not RenderNodeDrawable renderNodeDrawable)
                {
                    inputSocket.Value = renderNodeDrawable = new RenderNodeDrawable();
                    ((IModifiableHierarchical)inputSocket).AddChild(renderNodeDrawable);
                }

                renderNodeDrawable.Node = node;

                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Transform?> AcceptMatrix(this InputSocket<Transform?> inputSocket)
    {
        // Matrixを受け取った時MatrixTransformに変換する
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = null;
            if (obj == null)
            {
                return true;
            }

            if (obj is Transform transform)
            {
                if (inputSocket.Value is MatrixTransform matrixTransform &&
                    !ReferenceEquals(transform, matrixTransform))
                {
                    ((IModifiableHierarchical)inputSocket).RemoveChild(matrixTransform);
                }

                value = transform;
                return true;
            }

            if (obj is Matrix matrix)
            {
                if (inputSocket.Value is not MatrixTransform matrixTransform)
                {
                    inputSocket.Value = matrixTransform = new MatrixTransform();
                    ((IModifiableHierarchical)inputSocket).AddChild(matrixTransform);
                }

                matrixTransform.Matrix.CurrentValue = matrix;

                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<T> AcceptNumber<T>(this InputSocket<T> inputSocket)
        where T : INumber<T>
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber<T>(obj, out T? numValue))
            {
                value = numValue;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Thickness> AcceptNumber(this InputSocket<Thickness> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out float numValue))
            {
                value = new Thickness(numValue, numValue);
                return true;
            }

            if (obj is string str
                && Thickness.TryParse(str, out Thickness parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Vector> AcceptNumber(this InputSocket<Vector> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out float numValue))
            {
                value = new Vector(numValue, numValue);
                return true;
            }

            if (obj is Size size)
            {
                value = new Vector(size.Width, size.Height);
                return true;
            }

            if (obj is Point point)
            {
                value = new Vector(point.X, point.Y);
                return true;
            }

            if (obj is string str
                && Vector.TryParse(str, out Vector parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Point> AcceptNumber(this InputSocket<Point> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out float numValue))
            {
                value = new Point(numValue, numValue);
                return true;
            }

            if (obj is Size size)
            {
                value = new Point(size.Width, size.Height);
                return true;
            }

            if (obj is Vector vec)
            {
                value = new Point(vec.X, vec.Y);
                return true;
            }

            if (obj is string str
                && Point.TryParse(str, out Point parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Size> AcceptNumber(this InputSocket<Size> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out float numValue))
            {
                value = new Size(numValue, numValue);
                return true;
            }

            if (obj is Point point)
            {
                value = new Size(point.X, point.Y);
                return true;
            }

            if (obj is Vector vec)
            {
                value = new Size(vec.X, vec.Y);
                return true;
            }

            if (obj is string str
                && Size.TryParse(str, out Size parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<Rect> AcceptNumber(this InputSocket<Rect> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out float numValue))
            {
                value = new Rect(numValue, numValue, numValue, numValue);
                return true;
            }

            if (obj is Size size)
            {
                value = new Rect(size);
                return true;
            }

            if (obj is string str
                && Rect.TryParse(str, out Rect parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<PixelPoint> AcceptNumber(this InputSocket<PixelPoint> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out int numValue))
            {
                value = new PixelPoint(numValue, numValue);
                return true;
            }

            if (obj is PixelSize size)
            {
                value = new PixelPoint(size.Width, size.Height);
                return true;
            }

            if (obj is string str
                && PixelPoint.TryParse(str, out PixelPoint parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<PixelSize> AcceptNumber(this InputSocket<PixelSize> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out int numValue))
            {
                value = new PixelSize(numValue, numValue);
                return true;
            }

            if (obj is PixelPoint point)
            {
                value = new PixelSize(point.X, point.Y);
                return true;
            }

            if (obj is string str
                && PixelSize.TryParse(str, out PixelSize parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    public static InputSocket<PixelRect> AcceptNumber(this InputSocket<PixelRect> inputSocket)
    {
        inputSocket.RegisterReceiver((obj, out value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }

            if (ToNumber(obj, out int numValue))
            {
                value = new PixelRect(numValue, numValue, numValue, numValue);
                return true;
            }

            if (obj is PixelSize size)
            {
                value = new PixelRect(size);
                return true;
            }

            if (obj is string str
                && PixelRect.TryParse(str, out PixelRect parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        });

        return inputSocket;
    }

    private static bool ToNumber<T>(object? obj, out T value)
        where T : INumber<T>
    {
        try
        {
            if (obj is double @double)
            {
                value = T.CreateTruncating(@double);
            }
            else if (obj is Half half)
            {
                value = T.CreateTruncating(half);
            }
            else if (obj is short @short)
            {
                value = T.CreateTruncating(@short);
            }
            else if (obj is long @long)
            {
                value = T.CreateTruncating(@long);
            }
            else if (obj is Int128 int128)
            {
                value = T.CreateTruncating(int128);
            }
            else if (obj is nint nint)
            {
                value = T.CreateTruncating(nint);
            }
            else if (obj is sbyte @sbyte)
            {
                value = T.CreateTruncating(@sbyte);
            }
            else if (obj is float @float)
            {
                value = T.CreateTruncating(@float);
            }
            else if (obj is char @char)
            {
                value = T.CreateTruncating(@char);
            }
            else if (obj is decimal @decimal)
            {
                value = T.CreateTruncating(@decimal);
            }
            else if (obj is ushort @ushort)
            {
                value = T.CreateTruncating(@ushort);
            }
            else if (obj is uint @uint)
            {
                value = T.CreateTruncating(@uint);
            }
            else if (obj is ulong @ulong)
            {
                value = T.CreateTruncating(@ulong);
            }
            else if (obj is UInt128 uInt128)
            {
                value = T.CreateTruncating(uInt128);
            }
            else if (obj is nuint nuint)
            {
                value = T.CreateTruncating(nuint);
            }
            else if (obj is byte @byte)
            {
                value = T.CreateTruncating(@byte);
            }
            else if (obj is int @int)
            {
                value = T.CreateTruncating(@int);
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
