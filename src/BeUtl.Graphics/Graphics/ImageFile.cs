using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Validation;

namespace BeUtl.Graphics;

public class ImageFile : Drawable
{
    public static readonly CoreProperty<FileInfo?> SourceFileProperty;
    private FileInfo? _sourceFile;
    private IBitmap? _bitmap;

    static ImageFile()
    {
        SourceFileProperty = ConfigureProperty<FileInfo?, ImageFile>(nameof(SourceFile))
            .Accessor(o => o.SourceFile, (o, v) => o.SourceFile = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Validator(new FileInfoExtensionValidator()
            {
                FileExtensions = new[] { "bmp", "gif", "ico", "jpg", "jpeg", "png", "wbmp", "webp", "pkm", "ktx", "astc", "dng", "heif" }
            })
            .Register();

        AffectsRender<ImageFile>(SourceFileProperty);
    }

    public FileInfo? SourceFile
    {
        get => _sourceFile;
        set
        {
            if (SetAndRaise(SourceFileProperty, ref _sourceFile, value))
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobj
            && jobj.TryGetPropertyValue("source-file", out JsonNode? fileNode)
            && fileNode is JsonValue fileValue
            && fileValue.TryGetValue(out string? fileStr)
            && File.Exists(fileStr))
        {
            SourceFile = new FileInfo(fileStr);
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobj
            && _sourceFile != null)
        {
            jobj["source-file"] = _sourceFile.FullName;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (TryLoadBitmap())
        {
            return new Size(_bitmap.Width, _bitmap.Height);
        }
        else
        {
            return default;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (TryLoadBitmap())
        {
            canvas.DrawBitmap(_bitmap);
        }
    }

    [MemberNotNullWhen(true, "_bitmap")]
    private bool TryLoadBitmap()
    {
        if (_sourceFile?.Exists == true)
        {
            try
            {
                if (_bitmap?.IsDisposed != false)
                {
                    _bitmap = Bitmap<Bgra8888>.FromFile(_sourceFile.FullName);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return false;
        }
    }
}
