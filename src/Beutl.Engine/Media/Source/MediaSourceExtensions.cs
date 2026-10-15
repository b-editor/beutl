using Beutl.Serialization;

namespace Beutl.Media.Source;

public static class MediaSourceExtensions
{
    extension<T>(T) where T : MediaSource, new()
    {
        public static T Open(string fileName) => Open<T>(UriHelper.CreateFromPath(fileName));

        public static T Open(Uri uri)
        {
            var source = new T();
            source.ReadFrom(uri);
            return source;
        }
    }
}
