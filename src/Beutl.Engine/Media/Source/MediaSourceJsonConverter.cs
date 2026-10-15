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
            // MediaSource is abstract, so there is nothing to create.
            throw new InvalidOperationException("Cannot create instance of abstract type MediaSource.");
        }

        return Activator.CreateInstance(typeToConvert) as MediaSource;
    }
}
