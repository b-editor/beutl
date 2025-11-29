using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class SoundSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(ISoundSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(ISoundSource)
            ? new SoundSource()
            : Activator.CreateInstance(typeToConvert) as ISoundSource;
    }
}
