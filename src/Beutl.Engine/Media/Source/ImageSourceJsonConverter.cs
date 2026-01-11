using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class ImageSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(ImageSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(ImageSource)
            ? new ImageSource()
            : Activator.CreateInstance(typeToConvert) as ImageSource;
    }
}
