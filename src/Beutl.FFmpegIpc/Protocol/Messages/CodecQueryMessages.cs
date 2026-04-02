namespace Beutl.FFmpegIpc.Protocol.Messages;

public sealed class QueryCodecsRequest
{
    /// <summary>"video" or "audio"</summary>
    public string MediaType { get; set; } = "video";
}

public sealed class QueryCodecsResponse
{
    public CodecInfo[] Codecs { get; set; } = [];
}

public sealed class CodecInfo
{
    public string Name { get; set; } = "";
    public string LongName { get; set; } = "";
}

public sealed class QueryPixelFormatsRequest
{
    public string? CodecName { get; set; }
    public string? OutputFile { get; set; }
}

public sealed class QueryPixelFormatsResponse
{
    public PixelFormatInfo[] Formats { get; set; } = [];
}

public sealed class PixelFormatInfo
{
    public int Value { get; set; }
    public string Name { get; set; } = "";
}

public sealed class QuerySampleRatesRequest
{
    public string? CodecName { get; set; }
    public string? OutputFile { get; set; }
}

public sealed class QuerySampleRatesResponse
{
    public int[] SampleRates { get; set; } = [];
}

public sealed class QueryAudioFormatsRequest
{
    public string? CodecName { get; set; }
    public string? OutputFile { get; set; }
}

public sealed class QueryAudioFormatsResponse
{
    public int[] Formats { get; set; } = [];
}

public sealed class QueryDefaultCodecRequest
{
    public string OutputFile { get; set; } = "";
}

public sealed class QueryDefaultCodecResponse
{
    public string? VideoCodecName { get; set; }
    public string? AudioCodecName { get; set; }
}
