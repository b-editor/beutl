using Beutl.IO;

namespace Beutl.Graphics3D.Models;

public sealed class ModelSourceJsonConverter : FileSourceJsonConverter
{
    protected override void ReadFrom(IFileSource instance, Uri uri)
    {
        try
        {
            base.ReadFrom(instance, uri);
        }
        catch (Exception ex) when (uri.IsFile
            && ex is FileNotFoundException or InvalidOperationException { InnerException: FileNotFoundException })
        {
            // ReadFrom retains the URI before loading. Keep the source and the
            // serialized mesh edits available for the missing-media dialog.
        }
    }

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
