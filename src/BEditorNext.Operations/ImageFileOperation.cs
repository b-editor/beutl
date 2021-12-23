using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations;

public sealed class ImageFileOperation : RenderOperation
{
    public static readonly PropertyDefine<FileInfo?> FileProperty;
    private Bitmap<Bgra8888>? _cache;
    private RenderableBitmap? _latest;

    static ImageFileOperation()
    {
        FileProperty = RegisterProperty<FileInfo?, ImageFileOperation>(nameof(File), (owner, obj) => owner.File = obj, owner => owner.File)
            .EnableEditor()
            .SuppressAutoRender(true)
            .JsonName("file")
            .FilePicker("ImageFileString", "bmp", "gif", "ico", "jpg", "jpeg", "png", "wbmp", "webp", "pkm", "ktx", "astc", "dng", "heif")
            .Header("ImageFileString");
    }

    public ImageFileOperation()
    {
        if (Setters.FirstOrDefault(i => i.Property == FileProperty) is Setter<FileInfo?> setter)
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
            _latest = new RenderableBitmap((Bitmap<Bgra8888>)_cache.Clone());
        }
        else
        {
            _latest.Update((Bitmap<Bgra8888>)_cache.Clone());
        }

        args.List.Add(_latest);
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        FileChanged(File);
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
