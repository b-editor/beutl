using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

public sealed partial class ShaderGraphBuilder
{
    /// <summary>Declares a fixed-size image, including arrays, volumes, cubes and mip chains.</summary>
    public ShaderImage Image(string name, ImageDescription description)
    {
        EnsureRecording();
        description.Validate();
        AddName(_resourceNames, name);
        var image = new ShaderImage(this, _resources.Count, name, description.Format, null,
            new Rect(0, 0, description.Width, description.Height), 0, 1, description);
        _resources.Add(image);
        return image;
    }

    /// <summary>Selects mip levels and array layers without copying their storage. Offsets are relative to the supplied view.</summary>
    /// <remarks>Use a single 2D layer and mip as an attachment or output; storage bindings select one mip.</remarks>
    public ShaderImage View(string name, ShaderImage image, ImageKind kind, int baseMip = 0, int mipLevels = 1,
        int baseLayer = 0, int arrayLayers = 1)
    {
        EnsureRecording();
        ValidateResource(image);
        ArgumentOutOfRangeException.ThrowIfNegative(baseMip);
        ArgumentOutOfRangeException.ThrowIfNegative(baseLayer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipLevels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayLayers);
        if (baseMip > image.MipLevels - mipLevels || baseLayer > image.ArrayLayers - arrayLayers)
            throw new ArgumentException("Image view is outside the supplied subresources.");
        bool volume = image.Kind == ImageKind.Image3D;
        if (!Enum.IsDefined(kind) || volume != (kind == ImageKind.Image3D)
            || kind == ImageKind.Image2D && arrayLayers != 1
            || kind is ImageKind.Cube or ImageKind.CubeArray
                && (image.Root.Kind is not (ImageKind.Cube or ImageKind.CubeArray)
                    || (image.BaseArrayLayer + baseLayer) % 6 != 0 || arrayLayers % 6 != 0)
            || kind == ImageKind.Cube && arrayLayers != 6)
            throw new ArgumentException("Image view kind is incompatible with its backing image or layer range.");
        AddName(_resourceNames, name);
        ImageDescription? description = image.Description;
        if (description is { } desc)
        {
            (int width, int height, int depth) = desc.MipExtent(baseMip);
            description = desc with
            {
                Kind = kind,
                Width = width,
                Height = height,
                Depth = depth,
                ArrayLayers = arrayLayers,
                MipLevels = mipLevels
            };
        }
        else if (kind != ImageKind.Image2D)
            throw new ArgumentException("A composition-sized image has one 2D layer.");
        var view = new ShaderImage(this, _resources.Count, name, image.Format, image,
            description is { } size ? new Rect(0, 0, size.Width, size.Height) : null, 0,
            description.HasValue ? 1 : image.ResolutionScale, description, image.Root,
            image.BaseMipLevel + baseMip, image.BaseArrayLayer + baseLayer);
        _resources.Add(view);
        return view;
    }

    /// <summary>Clears every subresource in the view to a typed color.</summary>
    public void Clear(string name, ShaderImage image, ImageClearValue value = default)
    {
        ValidateResource(image);
        if (image.Format.IsDepthFormat() || image.Format.GetNumericType() != value.Category)
            throw new ArgumentException("Clear components must match the image's numeric type.", nameof(value));
        AddTransfer(name, new(ShaderTransferKind.ClearImage, null, image, value));
    }

    public void ClearDepth(string name, ShaderImage image, float depth = 1, uint stencil = 0)
    {
        ValidateResource(image);
        if (!image.Format.IsDepthFormat() || !float.IsFinite(depth) || depth < 0 || depth > 1)
            throw new ArgumentException("A depth image and a depth value between zero and one are required.");
        AddTransfer(name, new(ShaderTransferKind.ClearDepth, null, image, Depth: depth, Value: stencil));
    }

    /// <summary>Fills the buffer with a repeated 32-bit word. Its byte length must be a multiple of four.</summary>
    public void Clear(string name, ShaderBuffer buffer, uint value = 0)
    {
        ValidateResource(buffer);
        if (buffer.ByteLength % 4 != 0)
            throw new ArgumentException("A word fill requires a buffer size divisible by four.", nameof(buffer));
        AddTransfer(name, new(ShaderTransferKind.ClearBuffer, null, buffer, Value: value));
    }

    /// <summary>Copies all selected subresources. Source and destination must have matching formats and extents.</summary>
    public void Copy(string name, ShaderImage source, ShaderImage destination)
    {
        ValidateResource(source);
        ValidateResource(destination);
        if (source.Format != destination.Format || source.MipLevels != destination.MipLevels
            || source.Depth != destination.Depth || source.ArrayLayers != destination.ArrayLayers
            || source.Overlaps(destination))
            throw new ArgumentException("Image copies require matching, non-overlapping subresources.");
        AddTransfer(name, new(ShaderTransferKind.CopyImage, source, destination));
    }

    /// <summary>Copies the complete buffer.</summary>
    public void Copy(string name, ShaderBuffer source, ShaderBuffer destination)
    {
        ValidateResource(source);
        ValidateResource(destination);
        if (source.ByteLength != destination.ByteLength || ReferenceEquals(source, destination))
            throw new ArgumentException("Buffer copies require distinct buffers with equal byte lengths.");
        AddTransfer(name, new(ShaderTransferKind.CopyBuffer, source, destination));
    }

    /// <summary>Builds the remaining mip levels of a color view from its first level, for every selected layer.</summary>
    public void GenerateMipmaps(string name, ShaderImage image, SamplerFilter filter = SamplerFilter.Linear)
    {
        ValidateResource(image);
        if (image.Format.IsDepthFormat() || image.MipLevels < 2 || !Enum.IsDefined(filter)
            || image.Format.GetNumericType() != NumericType.Float && filter != SamplerFilter.Nearest)
            throw new ArgumentException("Mip generation requires a color mip chain; integer formats use nearest filtering.");
        AddTransfer(name, new(ShaderTransferKind.GenerateMipmaps, image, image, Filter: filter));
    }

    private void AddTransfer(string name, ShaderTransfer transfer)
    {
        EnsureRecording();
        if (transfer.Destination is ShaderImage image && ReferenceEquals(image.Root, Source))
            throw new ArgumentException("The source image is read-only.");
        AddName(_passNames, name);
        _passes.Add(new(name, null, [], [], [], BlendState.Replace, default, false) { Transfer = transfer });
    }
}

internal enum ShaderTransferKind { ClearImage, ClearDepth, ClearBuffer, CopyImage, CopyBuffer, GenerateMipmaps }

internal sealed record ShaderTransfer(ShaderTransferKind Kind, ShaderResource? Source, ShaderResource Destination,
    ImageClearValue Color = default, float Depth = 1, uint Value = 0, SamplerFilter Filter = SamplerFilter.Linear);
