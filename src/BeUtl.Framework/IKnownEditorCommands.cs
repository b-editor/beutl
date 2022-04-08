namespace BeUtl.Framework;

public interface IKnownEditorCommands
{
    ValueTask<bool> OnSave() => ValueTask.FromResult(false);

    ValueTask<bool> OnUndo() => ValueTask.FromResult(false);

    ValueTask<bool> OnRedo() => ValueTask.FromResult(false);

    ValueTask<bool> OnClose() => ValueTask.FromResult(false);
}
