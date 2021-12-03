namespace BEditorNext;

public interface IRecordableCommand
{
    ResourceReference<string> Name => "UnknownString";

    void Do();

    void Undo();

    void Redo();
}
