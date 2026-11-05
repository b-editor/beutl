using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Shaders;

public enum ShaderDataType
{
    Float32,
    Int32,
    UInt32,
    Float64,
    Int64,
    UInt64,

    [Display(Name = nameof(NodeGraphStrings.ShaderDataType_HexBytes), ResourceType = typeof(NodeGraphStrings))]
    HexBytes,
}

/// <summary>Writes numbers typed as text into bytes, for a buffer or an array in a shader.</summary>
public partial class ShaderDataNode : GraphNode
{
    public ShaderDataNode()
    {
        Output = AddOutput<byte[]>("Output", NodePortDisplays.Output);
        Text = AddInput<string>("Text", ShaderPortDisplays.Get("Text"));
        UseMultilineEditor(Text);
        DataType = AddInput<ShaderDataType>("DataType", ShaderPortDisplays.Get("DataType"));
        Error = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public OutputPort<byte[]> Output { get; }

    public InputPort<string> Text { get; }

    public InputPort<ShaderDataType> DataType { get; }

    public NodeMonitor<string?> Error { get; }

    public partial class Resource
    {
        private (string? Text, ShaderDataType Type, byte[]? Bytes, string? Error) _parsed;

        public override void Update(GraphCompositionContext context)
        {
            ShaderDataNode node = RequireOriginal();
            string text = Text ?? "";
            if (_parsed.Bytes == null || _parsed.Text != text || _parsed.Type != DataType)
            {
                try
                {
                    _parsed = (text, DataType, Parse(text, DataType), null);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    _parsed = (text, DataType, [], ex.Message);
                }
            }

            if (node.Error.Value != _parsed.Error) node.Error.Value = _parsed.Error;
            Output = _parsed.Bytes;
        }

        private static byte[] Parse(string text, ShaderDataType type)
        {
            string[] values = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            foreach (string value in values)
            {
                switch (type)
                {
                    case ShaderDataType.Float32: writer.Write(float.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.Int32: writer.Write(int.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.UInt32: writer.Write(uint.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.Float64: writer.Write(double.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.Int64: writer.Write(long.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.UInt64: writer.Write(ulong.Parse(value, CultureInfo.InvariantCulture)); break;
                    case ShaderDataType.HexBytes: writer.Write(Convert.FromHexString(value)); break;
                    default: throw new ArgumentOutOfRangeException(nameof(type));
                }
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}

/// <summary>Writes the numbers connected to it, one after another with no padding, into bytes.</summary>
public partial class ShaderPackNode : GraphNode
{
    public ShaderPackNode()
    {
        Output = AddOutput<byte[]>("Output", NodePortDisplays.Output);
        Values = AddListInput<object?>("Values", ShaderPortDisplays.Get("Values"));
    }

    public OutputPort<byte[]> Output { get; }

    public ListInputPort<object?> Values { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            using var stream = new MemoryStream();
            foreach (object? value in context.CollectListInputValues(RequireOriginal().Values))
            {
                switch (value)
                {
                    case bool v: Write(stream, v ? 1 : 0); break;
                    case int v: Write(stream, v); break;
                    case uint v: Write(stream, v); break;
                    case long v: Write(stream, v); break;
                    case ulong v: Write(stream, v); break;
                    case float v: Write(stream, v); break;
                    case double v: Write(stream, v); break;
                    case Vector2 v: Write(stream, v); break;
                    case Vector3 v: Write(stream, v); break;
                    case Vector4 v: Write(stream, v); break;
                    case byte[] v: stream.Write(v); break;
                }
            }

            Output = stream.ToArray();
        }

        private static void Write<T>(Stream stream, T value) where T : unmanaged
            => stream.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    }
}

/// <summary>Combines numbers into the vectors a shader declares.</summary>
public partial class ShaderVectorNode : GraphNode
{
    public ShaderVectorNode()
    {
        Vector2 = AddOutput<Vector2>("Vector2", ShaderPortDisplays.Get("Vec2"));
        Vector3 = AddOutput<Vector3>("Vector3", ShaderPortDisplays.Get("Vec3"));
        Vector4 = AddOutput<Vector4>("Vector4", ShaderPortDisplays.Get("Vec4"));
        X = AddInput<float>("X", ShaderPortDisplays.Get("X"));
        Y = AddInput<float>("Y", ShaderPortDisplays.Get("Y"));
        Z = AddInput<float>("Z", ShaderPortDisplays.Get("Z"));
        W = AddInput<float>("W", ShaderPortDisplays.Get("W"));
    }

    public OutputPort<Vector2> Vector2 { get; }

    public OutputPort<Vector3> Vector3 { get; }

    public OutputPort<Vector4> Vector4 { get; }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public InputPort<float> Z { get; }

    public InputPort<float> W { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Vector2 = new System.Numerics.Vector2(X, Y);
            Vector3 = new System.Numerics.Vector3(X, Y, Z);
            Vector4 = new System.Numerics.Vector4(X, Y, Z, W);
        }
    }
}

/// <summary>A color as the vec4 a shader works in: linear RGB and straight alpha.</summary>
public partial class ShaderColorNode : GraphNode
{
    public ShaderColorNode()
    {
        Output = AddOutput<Vector4>("Output", ShaderPortDisplays.Get("Vec4"));
        Color = AddInput<Color>("Color", ShaderPortDisplays.Get("Color"));
        Color.Property?.SetValue(Colors.White);
    }

    public OutputPort<Vector4> Output { get; }

    public InputPort<Color> Color { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Color color = Color;
            Output = new Vector4(
                Media.Color.SrgbToLinear(color.R / 255f),
                Media.Color.SrgbToLinear(color.G / 255f),
                Media.Color.SrgbToLinear(color.B / 255f),
                color.A / 255f);
        }
    }
}
