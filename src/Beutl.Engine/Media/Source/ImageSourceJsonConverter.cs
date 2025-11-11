using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class ImageSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IImageSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(IImageSource)
                ? new BitmapSource()
                : Activator.CreateInstance(typeToConvert) as IImageSource;
    }
}
