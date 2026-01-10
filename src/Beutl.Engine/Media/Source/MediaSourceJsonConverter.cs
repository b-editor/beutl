using Beutl.IO;

namespace Beutl.Media.Source;

public class MediaSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(MediaSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        if (typeToConvert == typeof(MediaSource))
        {
            // MediaSource is abstract, return null or throw
            throw new InvalidOperationException("Cannot create instance of abstract type MediaSource.");
        }

        if (typeToConvert == typeof(ImageSource))
        {
            return new ImageSource();
        }

        if (typeToConvert == typeof(SoundSource))
        {
            return new SoundSource();
        }

        if (typeToConvert == typeof(VideoSource))
        {
            return new VideoSource();
        }

        return Activator.CreateInstance(typeToConvert) as MediaSource;
    }
}
