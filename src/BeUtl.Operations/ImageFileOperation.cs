using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public sealed class ImageFileOperation : DrawableOperation
{
    public static readonly CoreProperty<FileInfo?> FileProperty;
    private readonly DrawableBitmap _latest = new();
    private Bitmap<Bgra8888>? _cache;

    static ImageFileOperation()
    {
        FileProperty = ConfigureProperty<FileInfo?, ImageFileOperation>(nameof(File))
            .Accessor(o => o.File, (o, v) => o.File = v)
            .OverrideMetadata(DefaultMetadatas.ImageFile)
            .Register();
    }

    public ImageFileOperation()
    {
        if (FindSetter(FileProperty) is PropertyInstance<FileInfo?> setter)
        {
            setter.GetObservable().Subscribe(f =>
            {
                FileChanged(f);
                ForceRender();
            });
        }
    }

    public FileInfo? File { get; set; }

    public override Drawable Drawable => _latest;

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (FindSetter(FileProperty) is PropertyInstance<FileInfo?> setter)
        {
            FileChanged(setter.Value);
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        FileChanged(null);
    }

    private void FileChanged(FileInfo? file)
    {
        _cache?.Dispose();
        _cache = null;

        if (file?.Exists ?? false)
        {
            _cache = Bitmap<Bgra8888>.FromFile(file.FullName);
            _latest.Initialize(_cache);
        }
    }
}
