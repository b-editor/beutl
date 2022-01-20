namespace BeUtl.Styling;

public interface ISetterBatch
{
    IStyleable Target { get; }

    CoreProperty Property { get; }

    void Apply();
}
