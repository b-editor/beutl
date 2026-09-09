namespace Beutl.Services.AI;

internal sealed class SubtitleInputException : Exception
{
    public SubtitleInputException()
    {
    }

    public SubtitleInputException(string message)
        : base(message)
    {
    }

    public SubtitleInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
