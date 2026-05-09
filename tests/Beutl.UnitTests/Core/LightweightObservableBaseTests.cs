using Beutl.Reactive;

namespace Beutl.UnitTests.Core;

public class LightweightObservableBaseTests
{
    private sealed class TestObservable : LightweightObservableBase<int>
    {
        public int InitializeCount;
        public int DeinitializeCount;

        protected override void Initialize() => InitializeCount++;

        protected override void Deinitialize() => DeinitializeCount++;

        public void EmitNext(int value) => PublishNext(value);

        public void EmitCompleted() => PublishCompleted();

        public void EmitError(Exception error) => PublishError(error);
    }

    private sealed class RecordingObserver : IObserver<int>
    {
        public List<int> Values { get; } = [];
        public Exception? Error;
        public int CompletedCount;

        public void OnCompleted() => CompletedCount++;
        public void OnError(Exception error) => Error = error;
        public void OnNext(int value) => Values.Add(value);
    }

    [Test]
    public void Subscribe_FirstObserver_TriggersInitialize()
    {
        var obs = new TestObservable();
        var sub = obs.Subscribe(new RecordingObserver());

        Assert.That(obs.InitializeCount, Is.EqualTo(1));
        Assert.That(obs.DeinitializeCount, Is.EqualTo(0));

        sub.Dispose();
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
    }

    [Test]
    public void Subscribe_SecondObserver_DoesNotReinitialize()
    {
        var obs = new TestObservable();
        var s1 = obs.Subscribe(new RecordingObserver());
        var s2 = obs.Subscribe(new RecordingObserver());

        Assert.That(obs.InitializeCount, Is.EqualTo(1));

        s1.Dispose();
        Assert.That(obs.DeinitializeCount, Is.EqualTo(0));

        s2.Dispose();
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
    }

    [Test]
    public void Subscribe_Null_Throws()
    {
        var obs = new TestObservable();
        Assert.That(() => obs.Subscribe(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void OnNext_DispatchesToSingleObserver()
    {
        var obs = new TestObservable();
        var observer = new RecordingObserver();
        using var _ = obs.Subscribe(observer);

        obs.EmitNext(1);
        obs.EmitNext(2);

        Assert.That(observer.Values, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void OnNext_DispatchesToMultipleObservers()
    {
        var obs = new TestObservable();
        var a = new RecordingObserver();
        var b = new RecordingObserver();
        using var _ = obs.Subscribe(a);
        using var __ = obs.Subscribe(b);

        obs.EmitNext(7);

        Assert.That(a.Values, Is.EqualTo(new[] { 7 }));
        Assert.That(b.Values, Is.EqualTo(new[] { 7 }));
    }

    [Test]
    public void OnCompleted_NotifiesAllObserversAndDeinitializes()
    {
        var obs = new TestObservable();
        var observer = new RecordingObserver();
        using var _ = obs.Subscribe(observer);

        obs.EmitCompleted();

        Assert.That(observer.CompletedCount, Is.EqualTo(1));
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
    }

    [Test]
    public void OnError_NotifiesAllObservers()
    {
        var obs = new TestObservable();
        var observer = new RecordingObserver();
        using var _ = obs.Subscribe(observer);

        var error = new InvalidOperationException("boom");
        obs.EmitError(error);

        Assert.That(observer.Error, Is.SameAs(error));
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
    }

    [Test]
    public void Subscribe_AfterCompleted_OnCompletedImmediately()
    {
        var obs = new TestObservable();
        using (obs.Subscribe(new RecordingObserver()))
        {
            obs.EmitCompleted();
        }

        var observer = new RecordingObserver();
        var sub = obs.Subscribe(observer);
        Assert.That(observer.CompletedCount, Is.EqualTo(1));
        Assert.That(sub, Is.Not.Null);
    }

    [Test]
    public void Subscribe_AfterError_OnErrorImmediately()
    {
        var obs = new TestObservable();
        var firstObserver = new RecordingObserver();
        using (obs.Subscribe(firstObserver))
        {
            obs.EmitError(new InvalidOperationException("done"));
        }

        var observer = new RecordingObserver();
        obs.Subscribe(observer);

        Assert.That(observer.Error, Is.Not.Null);
    }

    [Test]
    public void Dispose_TwiceIsNoop()
    {
        var obs = new TestObservable();
        var sub = obs.Subscribe(new RecordingObserver());
        sub.Dispose();
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
        sub.Dispose();
        Assert.That(obs.DeinitializeCount, Is.EqualTo(1));
    }
}
