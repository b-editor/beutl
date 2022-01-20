namespace BeUtl.Styling;

public interface ISetter
{
    CoreProperty Property { get; }

    object? Value { get; }

    ISetterInstance Instance(IStyleable target);
}
