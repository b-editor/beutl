using System.Collections.Generic;

namespace BEditor.Extensions.AviUtl
{
    public static class Extensions
    {
        public static string ToStringCore(this PixelType type)
        {
            return type switch
            {
                PixelType.Color => "col",
                PixelType.Rgb => "rgb",
                PixelType.YCbCr => "yc",
                _ => type.ToString(),
            };
        }

        public static T GetArgValue<T>(this Dictionary<string, object> args, string key, T @default)
        {
            if (args.TryGetValue(key, out var value))
            {
                try
                {
                    var obj = (T)(dynamic)value;
                    return obj;
                }
                catch
                {
                    return @default;
                }
            }
            else
            {
                return @default;
            }
        }

        public static T GetArgValue<T>(this object[] args, int index, T @default)
        {
            if (index < args.Length)
            {
                try
                {
                    var obj = (T)(dynamic)args[index];
                    return obj;
                }
                catch
                {
                    return @default;
                }
            }
            else
            {
                return @default;
            }
        }
    }
}