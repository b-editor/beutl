using Beutl.IO;

namespace Beutl.Media.Source;

public sealed class SoundSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(SoundSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(SoundSource)
            ? new SoundSource()
            : Activator.CreateInstance(typeToConvert) as SoundSource;
    }
}
