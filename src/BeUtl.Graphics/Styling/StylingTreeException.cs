namespace BeUtl.Styling;

[Serializable]
public class StylingTreeException : Exception
{
    public StylingTreeException() { }

    public StylingTreeException(string message) : base(message) { }

    public StylingTreeException(string message, Exception inner) : base(message, inner) { }

    protected StylingTreeException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
