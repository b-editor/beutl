namespace BeUtl.Styling;

public interface IStyleable : IElement
{
    Styles Styles { get; set; }

    void StyleApplied(IStyleInstance instance);

    void InvalidateStyles();

    IStyleInstance? GetStyleInstance(IStyle style);
}
