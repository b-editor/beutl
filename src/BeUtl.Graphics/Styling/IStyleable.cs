namespace BeUtl.Styling;

public interface IStyleable : ICoreObject, ILogicalElement
{
    Styles Styles { get; set; }

    void StyleApplied(IStyleInstance instance);

    void InvalidateStyles();

    IStyleInstance? GetStyleInstance(IStyle style);
}
