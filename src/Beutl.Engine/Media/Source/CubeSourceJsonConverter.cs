using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class CubeSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(CubeSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return new CubeSource();
    }
}
