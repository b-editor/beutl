using System.Runtime.CompilerServices;

namespace Beutl;

// https://github.com/AvaloniaUI/Avalonia/blob/7d10558995ed298c7fd475505b8c9eb077896ba0/src/Avalonia.Base/EnumExtensions.cs#L9
/// <summary>
/// Provides extension methods for enums.
/// </summary>
public static class EnumExtensions
{

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool HasAllFlags<T>(this T value, T flags) where T : unmanaged, Enum
    {
        if (sizeof(T) == 1)
        {
            byte byteValue = Unsafe.As<T, byte>(ref value);
            byte byteFlags = Unsafe.As<T, byte>(ref flags);
            return (byteValue & byteFlags) == byteFlags;
        }
        else if (sizeof(T) == 2)
        {
            short shortValue = Unsafe.As<T, short>(ref value);
            short shortFlags = Unsafe.As<T, short>(ref flags);
            return (shortValue & shortFlags) == shortFlags;
        }
        else if (sizeof(T) == 4)
        {
            int intValue = Unsafe.As<T, int>(ref value);
            int intFlags = Unsafe.As<T, int>(ref flags);
            return (intValue & intFlags) == intFlags;
        }
        else if (sizeof(T) == 8)
        {
            long longValue = Unsafe.As<T, long>(ref value);
            long longFlags = Unsafe.As<T, long>(ref flags);
            return (longValue & longFlags) == longFlags;
        }
        else
            throw new NotSupportedException("Enum with size of " + Unsafe.SizeOf<T>() + " are not supported");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool HasAnyFlag<T>(this T value, T flags) where T : unmanaged, Enum
    {
        if (sizeof(T) == 1)
        {
            byte byteValue = Unsafe.As<T, byte>(ref value);
            byte byteFlags = Unsafe.As<T, byte>(ref flags);
            return (byteValue & byteFlags) != 0;
        }
        else if (sizeof(T) == 2)
        {
            short shortValue = Unsafe.As<T, short>(ref value);
            short shortFlags = Unsafe.As<T, short>(ref flags);
            return (shortValue & shortFlags) != 0;
        }
        else if (sizeof(T) == 4)
        {
            int intValue = Unsafe.As<T, int>(ref value);
            int intFlags = Unsafe.As<T, int>(ref flags);
            return (intValue & intFlags) != 0;
        }
        else if (sizeof(T) == 8)
        {
            long longValue = Unsafe.As<T, long>(ref value);
            long longFlags = Unsafe.As<T, long>(ref flags);
            return (longValue & longFlags) != 0;
        }
        else
            throw new NotSupportedException("Enum with size of " + Unsafe.SizeOf<T>() + " are not supported");
    }
}
