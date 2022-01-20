namespace BeUtl.Styling;

public interface IStyle
{
    IList<ISetter> Setters { get; }

    IStyleInstance Instance(IStyleable target, IStyleInstance? baseStyle = null);
}
