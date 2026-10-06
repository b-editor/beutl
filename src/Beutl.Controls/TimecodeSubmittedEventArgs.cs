namespace Beutl.Controls;

/// <summary>
/// Event args for <see cref="Player.CurrentTimeSubmitted"/>. Subscribers must call
/// <see cref="Accept"/> to consume the submission (closes the editor) or
/// <see cref="Reject"/> with a localized message to keep the editor open and
/// display the message as a validation tooltip.
/// </summary>
public sealed class TimecodeSubmittedEventArgs : EventArgs
{
    public TimecodeSubmittedEventArgs(string input)
    {
        Input = input ?? string.Empty;
    }

    public string Input { get; }

    public bool Handled { get; private set; }

    public string? Error { get; private set; }

    public void Accept()
    {
        Handled = true;
        Error = null;
    }

    public void Reject(string error)
    {
        Handled = false;
        Error = error ?? string.Empty;
    }
}
