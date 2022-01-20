namespace BeUtl.Styling;

public interface IStyleable : ICoreObject
{
    IList<IStyle> Styles { get; }

    IStyleInstance? GetStyleInstance(IStyle style);
}
