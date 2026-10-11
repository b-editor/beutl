using System.Reactive.Linq;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class SharedApiStateSubscriptionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedState_AllowsConcurrentSubscriptionAndDisposal(bool authentication)
    {
        using var http = new HttpClient();
        await using var application = new BeutlApiApplication(http, new ExtensionProvider());
        IObservable<object?> state = authentication
            ? application.AuthenticatedUser.Select(user => (object?)user)
            : application.GetResource<IAiEntitlementService>().Entitlements.Select(snapshot => (object?)snapshot);
        const int workerCount = 4;
        const int subscriptionsPerWorker = 5000;
        using var start = new Barrier(workerCount);
        int notifications = 0;
        Task[] workers = Enumerable.Range(0, workerCount).Select(_ => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < subscriptionsPerWorker; i++)
            {
                using IDisposable subscription = state.Subscribe(_ => Interlocked.Increment(ref notifications));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(workers);
        using IDisposable nextSubscription = state.Subscribe(_ => Interlocked.Increment(ref notifications));
        Assert.That(notifications, Is.EqualTo(workerCount * subscriptionsPerWorker + 1));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SharedState_ValueReadDoesNotWaitForSubscriber(bool initialReplay, bool boxed)
    {
        using var source = new ReactivePropertySlim<int>(1);
        using var state = new SynchronizedReadOnlyReactiveProperty<int>(source);
        using var readerHoldingGate = new ManualResetEventSlim();
        using var callbackEntered = new ManualResetEventSlim();
        var consumerGate = new object();
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        int expectedValue = initialReplay ? 1 : 2;
        IDisposable? subscription = initialReplay ? null : state.Subscribe(OnValue);

        // Catalog publication and job updates read Value under their own lock,
        // while the authentication callback needs that same lock.
        Task<object?> reader = Task.Factory.StartNew(() =>
        {
            lock (consumerGate)
            {
                readerHoldingGate.Set();
                if (!callbackEntered.Wait(timeout))
                    throw new TimeoutException("The subscriber did not receive the current value.");

                return boxed ? ((IReadOnlyReactiveProperty)state).Value : state.Value;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task delivery = Task.Factory.StartNew(() =>
        {
            if (!readerHoldingGate.Wait(timeout))
                throw new TimeoutException("The reader did not acquire the consumer lock.");

            if (initialReplay)
                subscription = state.Subscribe(OnValue);
            else
                source.Value = expectedValue;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await Task.WhenAll(reader, delivery).WaitAsync(timeout + timeout);
            Assert.That(await reader, Is.EqualTo(expectedValue));
        }
        finally
        {
            subscription?.Dispose();
        }

        void OnValue(int value)
        {
            if (value != expectedValue)
                return;

            callbackEntered.Set();
            // Bound the lock attempt so a regression releases the callback gate
            // and fails without leaving either worker blocked.
            if (!Monitor.TryEnter(consumerGate, timeout))
                throw new TimeoutException("Value reading blocked the subscriber's consumer lock.");

            Monitor.Exit(consumerGate);
        }
    }

    [Test]
    public void SharedState_PreservesSynchronousValueAndNotifications()
    {
        using var source = new ReactivePropertySlim<int>(1);
        using var state = new SynchronizedReadOnlyReactiveProperty<int>(source);
        var values = new List<int>();
        using IDisposable subscription = state.Subscribe(values.Add);
        object? sender = null;
        state.PropertyChanged += (value, args) =>
        {
            sender = value;
            Assert.That(args.PropertyName, Is.EqualTo("Value"));
        };

        source.Value = 2;

        Assert.Multiple(() =>
        {
            Assert.That(state.Value, Is.EqualTo(2));
            Assert.That(((IReadOnlyReactiveProperty)state).Value, Is.EqualTo(2));
            Assert.That(values, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(sender, Is.SameAs(state));
        });
    }

    [Test]
    public void SharedState_DisposalStopsObservationWithoutDisposingTheSource()
    {
        using var source = new ReactivePropertySlim<int>(1);
        using var state = new SynchronizedReadOnlyReactiveProperty<int>(source);
        var values = new List<int>();
        int completed = 0;
        using IDisposable subscription = state.Subscribe(values.Add, () => completed++);
        state.Dispose();
        source.Value = 2;

        Assert.Multiple(() =>
        {
            Assert.That(state.Value, Is.EqualTo(1));
            Assert.That(source.Value, Is.EqualTo(2));
            Assert.That(values, Is.EqualTo(new[] { 1 }));
            Assert.That(completed, Is.EqualTo(1));
        });
    }
}
