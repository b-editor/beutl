namespace Beutl.Services.StartupTasks;

public abstract class StartupTask
{
    public abstract Task Task { get; }
}
