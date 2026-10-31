using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics.Shaders;

/// <summary>Access performed by a pass, used to derive dependencies and synchronization.</summary>
[Flags]
public enum ResourceAccess
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}

/// <summary>The color interpretation at an explicit boundary. Intermediate images may contain arbitrary data.</summary>
/// <remarks>RGB uses sRGB primaries and D65 white. Linear variants omit the sRGB transfer function;
/// premultiplied variants multiply the encoded RGB components by alpha.</remarks>
public enum ColorEncoding
{
    LinearPremultiplied,
    LinearStraight,
    SrgbPremultiplied,
    SrgbStraight,
}

/// <summary>Immutable sampler configuration; it does not own a device sampler.</summary>
public readonly record struct SamplerState(
    SamplerFilter MinFilter,
    SamplerFilter MagFilter,
    SamplerAddressMode AddressU,
    SamplerAddressMode AddressV)
{
    public SamplerAddressMode AddressW { get; init; } = AddressU;
    public SamplerFilter MipmapFilter { get; init; } = MinFilter;
    public float MinLod { get; init; }
    public float MaxLod { get; init; } = 1000;
    public CompareFunction? Compare { get; init; }

    public static SamplerState LinearTransparent => new(
        SamplerFilter.Linear, SamplerFilter.Linear,
        SamplerAddressMode.ClampToBorder, SamplerAddressMode.ClampToBorder);

    public static SamplerState LinearClamp => new(
        SamplerFilter.Linear, SamplerFilter.Linear,
        SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);

    public static SamplerState NearestClamp => new(
        SamplerFilter.Nearest, SamplerFilter.Nearest,
        SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
}

/// <summary>Immutable blending configuration for the color attachments of a graphics pass.</summary>
public readonly record struct BlendState(
    bool Enabled,
    BlendFactor SourceColor,
    BlendFactor DestinationColor,
    BlendOp ColorOperation,
    BlendFactor SourceAlpha,
    BlendFactor DestinationAlpha,
    BlendOp AlphaOperation)
{
    public static BlendState Replace => new(false, BlendFactor.One, BlendFactor.Zero, BlendOp.Add,
        BlendFactor.One, BlendFactor.Zero, BlendOp.Add);

    public static BlendState PremultipliedAlpha => new(true, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
        BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOp.Add);

    internal PipelineOptions ToPipelineOptions()
    {
        PipelineOptions options = PipelineOptions.Fullscreen;
        options.BlendEnabled = Enabled;
        options.SrcColorBlendFactor = SourceColor;
        options.DstColorBlendFactor = DestinationColor;
        options.ColorBlendOp = ColorOperation;
        options.SrcAlphaBlendFactor = SourceAlpha;
        options.DstAlphaBlendFactor = DestinationAlpha;
        options.AlphaBlendOp = AlphaOperation;
        return options;
    }
}

/// <summary>A virtual resource belonging to one shader graph. Its storage is owned by the renderer.</summary>
public abstract class ShaderResource
{
    private protected ShaderResource(object owner, int id, string name)
    {
        Owner = owner;
        Id = id;
        Name = name;
    }

    internal object Owner { get; }
    internal int Id { get; }
    public string Name { get; }
}

/// <summary>A virtual image in composition coordinates, with a separately declared raster density.</summary>
public sealed class ShaderImage : ShaderResource
{
    internal ShaderImage(object owner, int id, string name, TextureFormat format,
        ShaderImage? like, Rect? bounds, float padding, float resolutionScale,
        ImageDescription? description = null, ShaderImage? root = null, int baseMip = 0, int baseLayer = 0)
        : base(owner, id, name)
    {
        Format = format;
        Like = like;
        ExplicitBounds = bounds;
        Padding = padding;
        ResolutionScale = resolutionScale;
        Description = description;
        Root = root ?? this;
        BaseMipLevel = baseMip;
        BaseArrayLayer = baseLayer;
    }

    public TextureFormat Format { get; }
    public float ResolutionScale { get; }
    public ImageKind Kind => Description?.Kind ?? ImageKind.Image2D;
    public int Depth => Description?.Depth ?? 1;
    public int ArrayLayers => Description?.ArrayLayers ?? 1;
    public int MipLevels => Description?.MipLevels ?? 1;
    internal ImageDescription? Description { get; }
    internal ShaderImage Root { get; }
    internal int BaseMipLevel { get; }
    internal int BaseArrayLayer { get; }
    internal bool IsView => !ReferenceEquals(Root, this);
    internal bool IsAttachment => Kind == ImageKind.Image2D && MipLevels == 1;
    internal IEnumerable<(int Image, int Mip, int Layer, int Aspect)> Subresources
    {
        get
        {
            for (int mip = BaseMipLevel; mip < BaseMipLevel + MipLevels; mip++)
                for (int layer = BaseArrayLayer; layer < BaseArrayLayer + ArrayLayers; layer++)
                {
                    yield return (Root.Id, mip, layer, Format.IsDepthFormat() ? 1 : 0);
                    if (Format.HasStencil()) yield return (Root.Id, mip, layer, 2);
                }
        }
    }

    internal bool Overlaps(ShaderImage other) => ReferenceEquals(Root, other.Root)
        && BaseMipLevel < other.BaseMipLevel + other.MipLevels && other.BaseMipLevel < BaseMipLevel + MipLevels
        && BaseArrayLayer < other.BaseArrayLayer + other.ArrayLayers && other.BaseArrayLayer < BaseArrayLayer + ArrayLayers;
    internal ShaderImage? Like { get; }
    internal ShaderImage? RasterSource { get; set; }

    /// <summary>The image whose raster frame this one shares, following <see cref="RasterSource"/> to its end.</summary>
    internal ShaderImage RasterRoot
    {
        get
        {
            ShaderImage raster = this;
            while (raster.RasterSource is { } inherited) raster = inherited;
            return raster;
        }
    }

    /// <summary>The fixed description, or a single 2D texel extent of <paramref name="size"/> in this format.</summary>
    internal ImageDescription DescriptionFor(PixelSize size)
        => Description ?? new(ImageKind.Image2D, size.Width, size.Height, Format);

    internal Rect? ExplicitBounds { get; }
    internal float Padding { get; }
    // Built-in radial sampling records affine extents around one origin for the complete input group.
    internal ImmutableArray<Matrix> BoundsTransforms { get; set; } = [];
    internal RelativePoint BoundsOrigin { get; set; } = RelativePoint.Center;

    internal Rect ResolveBounds(Rect sourceBounds, Rect? referenceBounds = null)
    {
        Rect bounds = Id == 0 ? sourceBounds : (ExplicitBounds ?? Like!.ResolveBounds(sourceBounds, referenceBounds)).Inflate(Padding);
        if (BoundsTransforms.IsEmpty || bounds.IsEmpty)
            return bounds;
        Rect reference = referenceBounds ?? sourceBounds;
        Point origin = reference.Position + BoundsOrigin.ToPixels(reference.Size);
        Rect result = bounds;
        foreach (Matrix transform in BoundsTransforms)
            result = result.Union(bounds.Translate(-origin).TransformToAABB(transform).Translate(origin));
        return result;
    }
}

/// <summary>A virtual buffer. Initial values, when supplied, are copied during graph recording.</summary>
public sealed class ShaderBuffer : ShaderResource
{
    internal ShaderBuffer(object owner, int id, string name, int count, int stride, ImmutableArray<byte> data)
        : base(owner, id, name)
    {
        Count = count;
        Stride = stride;
        InitialData = data;
    }

    public int Count { get; }
    public int Stride { get; }
    public long ByteLength => checked((long)Count * Stride);
    internal ImmutableArray<byte> InitialData { get; }
}

/// <summary>The selected image and its color interpretation at the graph boundary.</summary>
public sealed class ShaderGraphOutput
{
    internal ShaderGraphOutput(ShaderImage image, ColorEncoding color, ShaderImage? renderingImage = null)
    {
        Image = image;
        Color = color;
        RenderingImage = renderingImage ?? image;
    }

    public ShaderImage Image { get; }
    public ColorEncoding Color { get; }
    internal ShaderImage RenderingImage { get; }
}
