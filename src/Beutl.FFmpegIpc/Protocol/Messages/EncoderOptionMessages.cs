namespace Beutl.FFmpegIpc.Protocol.Messages;

public sealed class QueryEncoderOptionsRequest
{
    public string? CodecName { get; set; }
    public string? OutputFile { get; set; }
    public int PixelFormat { get; set; } = -1;
}

public sealed class QueryEncoderOptionsResponse
{
    public EncoderOptionInfo[] Options { get; set; } = [];
    public bool Degraded { get; set; }
}

public enum EncoderOptionKind
{
    Text,
    Integer,
    Number,
    Boolean,
    Choice,
}

public sealed class EncoderOptionInfo
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public EncoderOptionKind Kind { get; set; }
    public bool AllowsNumericValues { get; set; }
    public bool RequiresInteger { get; set; }
    public string? DefaultValue { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public EncoderOptionChoiceInfo[] Choices { get; set; } = [];
}

public sealed class EncoderOptionChoiceInfo
{
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public long? NumericValue { get; set; }
}
