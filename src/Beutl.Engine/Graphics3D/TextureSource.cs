using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics3D;

[JsonConverter(typeof(TextureSourceJsonConverter))]
public interface ITextureSource : IMediaSource
{
    PixelSize FrameSize { get; }

    bool Read(Device device, [NotNullWhen(true)] out Texture? texture);

    bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap);

    new ITextureSource Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}

public sealed class TextureSourceJsonConverter : JsonConverter<ITextureSource?>
{
    public override ITextureSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();

        return s != null && FileTextureSource.TryOpen(s, out var imageSource)
            ? imageSource
            : null;
    }

    public override void Write(Utf8JsonWriter writer, ITextureSource? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.Name);
    }
}

public abstract class TextureSource : ITextureSource
{
    public abstract PixelSize FrameSize { get; }

    public abstract bool IsDisposed { get; }

    public abstract string Name { get; }

    public abstract ITextureSource Clone();

    public abstract void Dispose();

    public abstract bool Read(Device device, [NotNullWhen(true)] out Texture? texture);

    public abstract bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap);
}

// TODO: 後で実装
public class FileTextureSource : TextureSource
{
    public override PixelSize FrameSize => throw new NotImplementedException();

    public override bool IsDisposed => throw new NotImplementedException();

    public override string Name => throw new NotImplementedException();

    public override ITextureSource Clone() => throw new NotImplementedException();

    public override void Dispose() => throw new NotImplementedException();

    public override bool Read(Device device, [NotNullWhen(true)] out Texture? texture) => throw new NotImplementedException();

    public override bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap) => throw new NotImplementedException();
}
