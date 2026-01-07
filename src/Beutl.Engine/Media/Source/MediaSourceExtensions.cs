using System.Diagnostics.CodeAnalysis;
using Beutl.Serialization;

namespace Beutl.Media.Source;

public static class MediaSourceExtensions
{
    extension<T>(T) where T : MediaSource, new()
    {
        public static T Open(string fileName)
        {
            var source = new T();
            source.ReadFrom(UriHelper.CreateFromPath(fileName));
            return source;
        }

        public static bool TryOpen(string fileName, [NotNullWhen(true)] out T? result)
        {
            try
            {
                result = new T();
                result.ReadFrom(UriHelper.CreateFromPath(fileName));
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        public static T Open(Uri uri)
        {
            var source = new T();
            source.ReadFrom(uri);
            return source;
        }

        public static bool TryOpen(Uri uri, [NotNullWhen(true)] out T? result)
        {
            try
            {
                result = new T();
                result.ReadFrom(uri);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }
    }
}
