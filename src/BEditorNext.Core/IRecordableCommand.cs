namespace BEditorNext;

public interface IRecordableCommand
{
    string Name => "Unknown command.";

    void Do();

    void Undo();

    void Redo();
}
