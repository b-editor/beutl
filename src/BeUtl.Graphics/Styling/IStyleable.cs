namespace BeUtl.Styling;

public interface IStyleable : ICoreObject, ILogicalElement, IStylingElement
{
    Styles Styles { get; set; }

    void StyleApplied(IStyleInstance instance);

    void InvalidateStyles();

    IStyleInstance? GetStyleInstance(IStyle style);
}
