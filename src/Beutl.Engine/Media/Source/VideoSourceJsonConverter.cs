using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class VideoSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IVideoSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(IVideoSource)
            ? new VideoSource()
            : Activator.CreateInstance(typeToConvert) as IVideoSource;
    }
}
