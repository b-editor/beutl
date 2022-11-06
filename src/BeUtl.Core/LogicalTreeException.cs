namespace Beutl;

[Serializable]
public class LogicalTreeException : Exception
{
    public LogicalTreeException() { }

    public LogicalTreeException(string message) : base(message) { }

    public LogicalTreeException(string message, Exception inner) : base(message, inner) { }

    protected LogicalTreeException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
