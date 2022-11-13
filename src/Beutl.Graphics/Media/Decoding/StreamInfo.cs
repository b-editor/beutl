namespace Beutl.Media.Decoding;

public abstract record StreamInfo(string CodecName, MediaType Type, Rational Duration);
