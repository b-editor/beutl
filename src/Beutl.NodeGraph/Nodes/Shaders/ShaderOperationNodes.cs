using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Fills an image with a color, or a depth image with a depth and stencil value.</summary>
public partial class ShaderClearImageNode : GraphNode
{
    public ShaderClearImageNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Value = AddInput<Vector4>("Value", ShaderPortDisplays.Get("Value"));
        Depth = AddInput<float>("Depth", NodePortDisplays.Depth);
        Depth.Property?.SetValue(1f);
        Stencil = AddInput<uint>("Stencil", ShaderPortDisplays.Get("Stencil"));
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<Vector4> Value { get; }

    public InputPort<float> Depth { get; }

    public InputPort<uint> Stencil { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? wire, Vector4 value, float depth, uint stencil) = (Image, Value, Depth, Stencil);
            Output = new(recording =>
            {
                ShaderImage image = recording.Require(wire, NodeGraphStrings.ShaderPort_Image);
                string name = recording.UniqueName("Clear");
                if (image.Format.IsDepthFormat())
                    recording.Builder.ClearDepth(name, image, depth, stencil);
                else
                    recording.Builder.Clear(name, image, ShaderImageContents.ValueOf(image.Format, value));
                return image;
            });
        }
    }
}

/// <summary>Fills a buffer with a repeated 32-bit word.</summary>
public partial class ShaderClearBufferNode : GraphNode
{
    public ShaderClearBufferNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderBuffer>>("Output", NodePortDisplays.Output);
        Buffer = AddInput<ShaderGraphValue<ShaderBuffer>>("Buffer", ShaderPortDisplays.Get("Buffer"));
        Value = AddInput<uint>("Value", ShaderPortDisplays.Get("Value"));
    }

    public OutputPort<ShaderGraphValue<ShaderBuffer>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderBuffer>> Buffer { get; }

    public InputPort<uint> Value { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderBuffer>? wire, uint value) = (Buffer, Value);
            Output = new(recording =>
            {
                ShaderBuffer buffer = recording.Require(wire, NodeGraphStrings.ShaderPort_Buffer);
                recording.Builder.Clear(recording.UniqueName("Clear"), buffer, value);
                return buffer;
            });
        }
    }
}

/// <summary>Copies an image into another of the same size and format.</summary>
public partial class ShaderCopyImageNode : GraphNode
{
    public ShaderCopyImageNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Source = AddInput<ShaderGraphValue<ShaderImage>>("Source", ShaderPortDisplays.Get("Source"));
        Destination = AddInput<ShaderGraphValue<ShaderImage>>("Destination", ShaderPortDisplays.Get("Destination"));
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Source { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Destination { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? source, ShaderGraphValue<ShaderImage>? destination) = (Source, Destination);
            Output = new(recording =>
            {
                ShaderImage from = recording.Require(source, NodeGraphStrings.ShaderPort_Source);
                ShaderImage to = recording.Require(destination, NodeGraphStrings.ShaderPort_Destination);
                recording.Builder.Copy(recording.UniqueName("Copy"), from, to);
                return to;
            });
        }
    }
}

/// <summary>Copies a buffer into another of the same length.</summary>
public partial class ShaderCopyBufferNode : GraphNode
{
    public ShaderCopyBufferNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderBuffer>>("Output", NodePortDisplays.Output);
        Source = AddInput<ShaderGraphValue<ShaderBuffer>>("Source", ShaderPortDisplays.Get("Source"));
        Destination = AddInput<ShaderGraphValue<ShaderBuffer>>("Destination", ShaderPortDisplays.Get("Destination"));
    }

    public OutputPort<ShaderGraphValue<ShaderBuffer>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderBuffer>> Source { get; }

    public InputPort<ShaderGraphValue<ShaderBuffer>> Destination { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderBuffer>? source, ShaderGraphValue<ShaderBuffer>? destination) = (Source, Destination);
            Output = new(recording =>
            {
                ShaderBuffer from = recording.Require(source, NodeGraphStrings.ShaderPort_Source);
                ShaderBuffer to = recording.Require(destination, NodeGraphStrings.ShaderPort_Destination);
                recording.Builder.Copy(recording.UniqueName("Copy"), from, to);
                return to;
            });
        }
    }
}

/// <summary>Builds an image's remaining mip levels from its first.</summary>
public partial class ShaderGenerateMipmapsNode : GraphNode
{
    public ShaderGenerateMipmapsNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        Filter = AddInput<SamplerFilter>("Filter", ShaderPortDisplays.Get("Filter"));
        Filter.Property?.SetValue(SamplerFilter.Linear);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<SamplerFilter> Filter { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? wire, SamplerFilter filter) = (Image, Filter);
            Output = new(recording =>
            {
                ShaderImage image = recording.Require(wire, NodeGraphStrings.ShaderPort_Image);
                recording.Builder.GenerateMipmaps(recording.UniqueName("Mipmaps"), image, filter);
                return image;
            });
        }
    }
}

/// <summary>Converts an image between color encodings; with nothing connected, the source image.</summary>
public partial class ShaderConvertColorNode : GraphNode
{
    public ShaderConvertColorNode()
    {
        Output = AddOutput<ShaderGraphValue<ShaderImage>>("Output", NodePortDisplays.Output);
        Image = AddInput<ShaderGraphValue<ShaderImage>>("Image", ShaderPortDisplays.Get("Image"));
        From = AddInput<ColorEncoding>("From", ShaderPortDisplays.Get("From"));
        To = AddInput<ColorEncoding>("To", ShaderPortDisplays.Get("To"));
        To.Property?.SetValue(ColorEncoding.SrgbStraight);
    }

    public OutputPort<ShaderGraphValue<ShaderImage>> Output { get; }

    public InputPort<ShaderGraphValue<ShaderImage>> Image { get; }

    public InputPort<ColorEncoding> From { get; }

    public InputPort<ColorEncoding> To { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            (ShaderGraphValue<ShaderImage>? wire, ColorEncoding from, ColorEncoding to) = (Image, From, To);
            Output = new(recording => recording.Builder.ConvertColor(recording.UniqueName("Convert"),
                wire != null ? recording.Evaluate(wire) : recording.Builder.Source, from, to));
        }
    }
}
