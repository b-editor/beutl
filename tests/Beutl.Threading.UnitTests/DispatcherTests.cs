using NUnit.Framework;

namespace Beutl.Threading.UnitTests;

public class DispatcherTests
{
    [Test]
    public void Invoke()
    {
        int id = Environment.CurrentManagedThreadId;
        var dispatcher = Dispatcher.Spawn();

        int dispatcherId = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.That(id, Is.Not.EqualTo(dispatcherId));

        dispatcher.Stop();
    }
}
