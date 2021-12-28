using NUnit.Framework;

namespace BEditorNext.Threading.UnitTests;

public class DispatcherTests
{
    [Test]
    public void Invoke()
    {
        int id = Environment.CurrentManagedThreadId;
        var dispatcher = Dispatcher.Spawn();

        int dispatcherId = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.False(id == dispatcherId);

        dispatcher.Stop();
    }
}
