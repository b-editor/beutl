namespace BeUtl;

[Serializable]
public class ElementException : Exception
{
    public ElementException() { }

    public ElementException(string message) : base(message) { }

    public ElementException(string message, Exception inner) : base(message, inner) { }

    protected ElementException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}