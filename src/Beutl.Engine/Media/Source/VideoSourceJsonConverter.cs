using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class VideoSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(VideoSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(VideoSource)
            ? new VideoSource()
            : Activator.CreateInstance(typeToConvert) as VideoSource;
    }
}
