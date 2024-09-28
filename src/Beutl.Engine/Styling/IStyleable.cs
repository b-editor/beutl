namespace Beutl.Styling;

public interface IStyleable : ICoreObject
{
    Styles Styles { get; set; }

    void StyleApplied(IStyleInstance instance);

    void InvalidateStyles();

    IStyleInstance? GetStyleInstance(IStyle style);
}
