using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public sealed class ImageFileOperation : LayerOperation
{
    public static readonly CoreProperty<FileInfo?> FileProperty;
    private Bitmap<Bgra8888>? _cache;
    private DrawableBitmap? _latest;

    static ImageFileOperation()
    {
        FileProperty = ConfigureProperty<FileInfo?, ImageFileOperation>(nameof(File))
            .Accessor(o => o.File, (o, v) => o.File = v)
            .EnableEditor()
            .SuppressAutoRender(true)
            .JsonName("file")
            .FilePicker("ImageFileString", "bmp", "gif", "ico", "jpg", "jpeg", "png", "wbmp", "webp", "pkm", "ktx", "astc", "dng", "heif")
            .Header("ImageFileString")
            .Register();
    }

    public ImageFileOperation()
    {
        if (FindSetter(FileProperty) is Setter<FileInfo?> setter)
        {
            setter.GetObservable().Subscribe(f =>
            {
                FileChanged(f);
                ForceRender();
            });
        }
    }

    public FileInfo? File { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        if (_cache?.IsDisposed ?? true) return;

        if (_latest == null)
        {
            _latest = new DrawableBitmap((Bitmap<Bgra8888>)_cache.Clone());
        }
        else
        {
            _latest.Initialize((Bitmap<Bgra8888>)_cache.Clone());
        }

        args.List.Add(_latest);
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (FindSetter(FileProperty) is Setter<FileInfo?> setter)
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
        }

        _latest?.Dispose();
        _latest = null;
    }
}
