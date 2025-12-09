namespace Beutl.Editor.Operations;

public interface IUpdatePropertyValueOperation
{
    CoreObject Object { get; }

    string PropertyPath { get; }

    object? NewValue { get; }

    object? OldValue { get; }
}
