using Beutl.IO;

namespace Beutl.Graphics3D.Models;

public sealed class ModelSourceJsonConverter : FileSourceJsonConverter
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(ModelSource));
    }

    public override IFileSource? CreateInstance(Type typeToConvert)
    {
        return typeToConvert == typeof(ModelSource)
            ? new ModelSource()
            : Activator.CreateInstance(typeToConvert) as ModelSource;
    }
}
