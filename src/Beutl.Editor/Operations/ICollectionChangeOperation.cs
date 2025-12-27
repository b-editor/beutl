namespace Beutl.Editor.Operations;

public interface ICollectionChangeOperation
{
    CoreObject Object { get; }

    string PropertyPath { get; }

    IEnumerable<object?> Items { get; }
}
