namespace BeUtl.Media.Decoding;

/// <summary>
/// 
/// </summary>
/// <param name="CodecName"></param>
/// <param name="Type"></param>
/// <param name="Duration">秒数</param>
public abstract record StreamInfo(string CodecName, MediaType Type, Rational Duration);
