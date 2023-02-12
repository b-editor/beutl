using System.Numerics;

namespace Beutl.NodeTree;

public static class InputSocketHelper
{
    public static InputSocket<T> AcceptNumber<T>(this InputSocket<T> inputSocket)
        where T : INumber<T>
    {
        inputSocket.RegisterReceiver((object? obj, out T? value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber<T>(obj, out T? numValue))
                {
                    value = numValue;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Graphics.Thickness> AcceptNumber(this InputSocket<Graphics.Thickness> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Graphics.Thickness value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out float numValue))
                {
                    value = new Graphics.Thickness(numValue, numValue);
                    return true;
                }
                else if (obj is string str
                    && Graphics.Thickness.TryParse(str, out Graphics.Thickness parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Graphics.Vector> AcceptNumber(this InputSocket<Graphics.Vector> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Graphics.Vector value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out float numValue))
                {
                    value = new Graphics.Vector(numValue, numValue);
                    return true;
                }
                else if (obj is Graphics.Size size)
                {
                    value = new Graphics.Vector(size.Width, size.Height);
                    return true;
                }
                else if (obj is Graphics.Point point)
                {
                    value = new Graphics.Vector(point.X, point.Y);
                    return true;
                }
                else if (obj is string str
                    && Graphics.Vector.TryParse(str, out Graphics.Vector parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Graphics.Point> AcceptNumber(this InputSocket<Graphics.Point> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Graphics.Point value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out float numValue))
                {
                    value = new Graphics.Point(numValue, numValue);
                    return true;
                }
                else if (obj is Graphics.Size size)
                {
                    value = new Graphics.Point(size.Width, size.Height);
                    return true;
                }
                else if (obj is Graphics.Vector vec)
                {
                    value = new Graphics.Point(vec.X, vec.Y);
                    return true;
                }
                else if (obj is string str
                    && Graphics.Point.TryParse(str, out Graphics.Point parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Graphics.Size> AcceptNumber(this InputSocket<Graphics.Size> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Graphics.Size value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out float numValue))
                {
                    value = new Graphics.Size(numValue, numValue);
                    return true;
                }
                else if (obj is Graphics.Point point)
                {
                    value = new Graphics.Size(point.X, point.Y);
                    return true;
                }
                else if (obj is Graphics.Vector vec)
                {
                    value = new Graphics.Size(vec.X, vec.Y);
                    return true;
                }
                else if (obj is string str
                    && Graphics.Size.TryParse(str, out Graphics.Size parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Graphics.Rect> AcceptNumber(this InputSocket<Graphics.Rect> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Graphics.Rect value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out float numValue))
                {
                    value = new Graphics.Rect(numValue, numValue, numValue, numValue);
                    return true;
                }
                else if (obj is Graphics.Size size)
                {
                    value = new Graphics.Rect(size);
                    return true;
                }
                else if (obj is string str
                    && Graphics.Rect.TryParse(str, out Graphics.Rect parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Media.PixelPoint> AcceptNumber(this InputSocket<Media.PixelPoint> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Media.PixelPoint value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out int numValue))
                {
                    value = new Media.PixelPoint(numValue, numValue);
                    return true;
                }
                else if (obj is Media.PixelSize size)
                {
                    value = new Media.PixelPoint(size.Width, size.Height);
                    return true;
                }
                else if (obj is string str
                    && Media.PixelPoint.TryParse(str, out Media.PixelPoint parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Media.PixelSize> AcceptNumber(this InputSocket<Media.PixelSize> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Media.PixelSize value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out int numValue))
                {
                    value = new Media.PixelSize(numValue, numValue);
                    return true;
                }
                else if (obj is Media.PixelPoint point)
                {
                    value = new Media.PixelSize(point.X, point.Y);
                    return true;
                }
                else if (obj is string str
                    && Media.PixelSize.TryParse(str, out Media.PixelSize parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        });

        return inputSocket;
    }

    public static InputSocket<Media.PixelRect> AcceptNumber(this InputSocket<Media.PixelRect> inputSocket)
    {
        inputSocket.RegisterReceiver((object? obj, out Media.PixelRect value) =>
        {
            value = default;
            if (obj == null)
            {
                return false;
            }
            else
            {
                if (ToNumber(obj, out int numValue))
                {
                    value = new Media.PixelRect(numValue, numValue, numValue, numValue);
                    return true;
                }
                else if (obj is Media.PixelSize size)
                {
                    value = new Media.PixelRect(size);
                    return true;
                }
                else if (obj is string str
                    && Media.PixelRect.TryParse(str, out Media.PixelRect parsed))
                {
                    value = parsed;
                    return true;
                }
                else
                {
                    return false;
                }
            }
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
            else if (obj is nint @nint)
            {
                value = T.CreateTruncating(@nint);
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
            else if (obj is nuint @nuint)
            {
                value = T.CreateTruncating(@nuint);
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
