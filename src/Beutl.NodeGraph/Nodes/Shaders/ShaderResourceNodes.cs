using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>The image the graph is applied to.</summary>
public partial class ShaderSourceNode : GraphNode
{
    public ShaderSourceNode()
    {
        Image = AddOutput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Image = new(recording => recording.Builder.Source);
        }
    }
}

/// <summary>Brings anything the graph renders to the shader wires, read at the same place as the source image.</summary>
public partial class ShaderPictureNode : GraphNode
{
    public ShaderPictureNode()
    {
        Image = AddOutput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Input = AddInput<RenderNode?>("Input", NodePortDisplays.Input);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<RenderNode?> Input { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            RenderNode? picture = Input;
            Image = new(recording => picture == null ? recording.Builder.Source : recording.ExternalImage(picture));
        }
    }
}

/// <summary>A new image that follows another image's position and size, or an explicit rectangle.</summary>
public partial class ShaderImageNode : GraphNode
{
    public ShaderImageNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Like = AddInput<ShaderGraphValue<ShaderImage>>("Like", ShaderPortDisplays.Get("Like"));
        Bounds = AddInput<Rect>("Bounds", ShaderPortDisplays.Get("Bounds"));
        Format = AddInput<TextureFormat>("Format", ShaderPortDisplays.Get("Format"));
        Format.Property?.SetValue(TextureFormat.RGBA16Float);
        Padding = AddInput<float>("Padding", ShaderPortDisplays.Get("Padding"));
        ResolutionScale = AddInput<float>("ResolutionScale", ShaderPortDisplays.Get("ResolutionScale"));
        ResolutionScale.Property?.SetValue(1f);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Like { get; }

    public InputPort<Rect> Bounds { get; }

    public InputPort<TextureFormat> Format { get; }

    public InputPort<float> Padding { get; }

    public InputPort<float> ResolutionScale { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? like, Rect bounds, TextureFormat format, float padding, float scale)
                = (Like, Bounds, Format, Padding, ResolutionScale);
            Output = new(recording =>
            {
                ShaderImage image = like == null && bounds.Width > 0 && bounds.Height > 0
                    ? recording.Builder.Image2D(recording.UniqueName("Image"), bounds, format, scale)
                    : ShaderImageContents.Declare(recording, "Image", like != null ? recording.Evaluate(like) : recording.Builder.Source,
                        format, padding, scale);
                return ShaderImageContents.Clear(recording, image);
            });
        }
    }
}

/// <summary>An image with a fixed number of texels, which may also have layers, depth or mip levels.</summary>
public partial class ShaderFixedImageNode : GraphNode
{
    public ShaderFixedImageNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Kind = AddInput<ImageKind>("Kind", ShaderPortDisplays.Get("Kind"));
        Width = AddInput<int>("Width", NodePortDisplays.Width);
        Width.Property?.SetValue(256);
        Height = AddInput<int>("Height", NodePortDisplays.Height);
        Height.Property?.SetValue(256);
        Depth = AddInput<int>("Depth", NodePortDisplays.Depth);
        Depth.Property?.SetValue(1);
        ArrayLayers = AddInput<int>("ArrayLayers", ShaderPortDisplays.Get("ArrayLayers"));
        ArrayLayers.Property?.SetValue(1);
        MipLevels = AddInput<int>("MipLevels", ShaderPortDisplays.Get("MipLevels"));
        MipLevels.Property?.SetValue(1);
        Format = AddInput<TextureFormat>("Format", ShaderPortDisplays.Get("Format"));
        Format.Property?.SetValue(TextureFormat.RGBA16Float);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ImageKind> Kind { get; }

    public InputPort<int> Width { get; }

    public InputPort<int> Height { get; }

    public InputPort<int> Depth { get; }

    public InputPort<int> ArrayLayers { get; }

    public InputPort<int> MipLevels { get; }

    public InputPort<TextureFormat> Format { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var description = new ImageDescription(Kind, Width, Height, Format, Depth, ArrayLayers, MipLevels);
            Output = new(recording => ShaderImageContents.Clear(recording,
                recording.Builder.Image(recording.UniqueName("Image"), description)));
        }
    }
}

/// <summary>Selects mip levels and layers of an image, without copying them.</summary>
public partial class ShaderImageViewNode : GraphNode
{
    public ShaderImageViewNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Kind = AddInput<ImageKind>("Kind", ShaderPortDisplays.Get("Kind"));
        BaseMip = AddInput<int>("BaseMip", ShaderPortDisplays.Get("BaseMip"));
        MipLevels = AddInput<int>("MipLevels", ShaderPortDisplays.Get("MipLevels"));
        MipLevels.Property?.SetValue(1);
        BaseLayer = AddInput<int>("BaseLayer", ShaderPortDisplays.Get("BaseLayer"));
        ArrayLayers = AddInput<int>("ArrayLayers", ShaderPortDisplays.Get("ArrayLayers"));
        ArrayLayers.Property?.SetValue(1);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<ImageKind> Kind { get; }

    public InputPort<int> BaseMip { get; }

    public InputPort<int> MipLevels { get; }

    public InputPort<int> BaseLayer { get; }

    public InputPort<int> ArrayLayers { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? image, ImageKind kind, int baseMip, int mipLevels, int baseLayer, int arrayLayers)
                = (Image, Kind, BaseMip, MipLevels, BaseLayer, ArrayLayers);
            Output = new(recording => recording.Builder.View(recording.UniqueName("View"),
                recording.Require(image, NodeGraphStrings.ShaderPort_Image), kind, baseMip, mipLevels, baseLayer, arrayLayers));
        }
    }
}

/// <summary>A buffer that starts with the given bytes, or with zeros.</summary>
public partial class ShaderBufferNode : GraphNode
{
    public ShaderBufferNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderBuffer>>("Output", NodePortDisplays.Output);
        Data = AddInput<byte[]>("Data", ShaderPortDisplays.Get("Data"));
        Count = AddInput<int>("Count", ShaderPortDisplays.Get("ElementCount"));
        Count.Property?.SetValue(1);
        Stride = AddInput<int>("Stride", ShaderPortDisplays.Get("Stride"));
        Stride.Property?.SetValue(4);
    }

    public OutputPort<ShaderGraphValue<ShaderBuffer>> Output { get; }

    public InputPort<byte[]> Data { get; }

    public InputPort<int> Count { get; }

    public InputPort<int> Stride { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (byte[]? data, int count, int stride) = (Data, Count, Stride);
            Output = new(recording =>
            {
                string name = recording.UniqueName("Buffer");
                if (data is { Length: > 0 })
                    return recording.Builder.Buffer(name, data, stride);
                ShaderBuffer buffer = recording.Builder.Buffer(name, count, stride);
                // A write-only shader has nothing to clear, but GLSL buffers read and write unless told otherwise.
                if (buffer.ByteLength % 4 == 0)
                    recording.Builder.Clear(recording.UniqueName($"{name}.clear"), buffer);
                return buffer;
            });
        }
    }
}

internal static class ShaderImageContents
{
    /// <summary>Declares an image that takes after <paramref name="like"/>.</summary>
    public static ShaderImage Declare(ShaderGraphRecordingContext recording, string name, ShaderImage like, TextureFormat format,
        float padding, float resolutionScale)
    {
        ShaderImage image = recording.Builder.Image2D(recording.UniqueName(name), like, format, padding, resolutionScale);
        // At the same size the two map texels one to one, whatever density the first arrived at.
        if (padding == 0 && resolutionScale == 1 && like.ResolutionScale == 1)
            image.RasterSource = like;
        return image;
    }

    /// <summary>Starts an image declared by a node with known contents, so a read never shows old memory.</summary>
    public static ShaderImage Clear(ShaderGraphRecordingContext recording, ShaderImage image)
    {
        string name = recording.UniqueName($"{image.Name}.clear");
        if (image.Format.IsDepthFormat())
            recording.Builder.ClearDepth(name, image);
        else
            recording.Builder.Clear(name, image, ZeroOf(image.Format));
        return image;
    }

    public static ImageClearValue ZeroOf(TextureFormat format) => ValueOf(format, default);

    public static ImageClearValue ValueOf(TextureFormat format, System.Numerics.Vector4 value) => format.GetNumericType() switch
    {
        NumericType.SInt => ImageClearValue.Int((int)value.X, (int)value.Y, (int)value.Z, (int)value.W),
        NumericType.UInt => ImageClearValue.UInt((uint)value.X, (uint)value.Y, (uint)value.Z, (uint)value.W),
        _ => ImageClearValue.Float(value)
    };
}
