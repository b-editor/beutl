namespace BeUtl;

public interface IRecordableCommand
{
    ResourceReference<string> Name => "UnknownString";

    void Do();

    void Undo();

    void Redo();
}

public static class RecordableCommandExtensions
{
    public static void DoAndRecord(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.DoAndPush(command);
    }

    public static void PushTo(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.PushOnly(command);
    }
}
