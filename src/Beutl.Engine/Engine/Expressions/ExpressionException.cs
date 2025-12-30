namespace Beutl.Engine.Expressions;

public class ExpressionException : Exception
{
    public ExpressionException(string message)
        : base(message)
    {
    }

    public ExpressionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
