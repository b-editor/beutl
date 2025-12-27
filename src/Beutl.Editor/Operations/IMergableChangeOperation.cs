namespace Beutl.Editor.Operations;

public interface IMergableChangeOperation
{
    bool TryMerge(ChangeOperation other);
}
