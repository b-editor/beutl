using System.Reactive.Linq;
using System.Reflection;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedState_EntitlementCallbackCanChangeAuthenticationSubscriptions(bool dispose)
    {
        using var http = new HttpClient();
        await using var application = new BeutlApiApplication(http, new ExtensionProvider());
        var profile = new Profile(new ProfileResponse
        {
            Id = "test-user",
            Name = "test",
            DisplayName = "Test User",
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, application);
        var owner = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, application, DateTime.UtcNow);
        FieldInfo authentication = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)authentication.GetValue(application)!).Value = owner;

        using var entitlementCallbackEntered = new ManualResetEventSlim();
        using var authenticationCallbackEntered = new ManualResetEventSlim();
        using var subscriptionChanged = new ManualResetEventSlim();
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        using IDisposable authenticationCallback = application.AuthenticatedUser.Subscribe(user =>
        {
            if (user is not null)
                return;

            authenticationCallbackEntered.Set();
            // Bound the overlap so the old locking fails without leaving a deadlock.
            if (!subscriptionChanged.Wait(timeout))
                throw new TimeoutException("The entitlement callback could not change authentication subscriptions.");
        });
        using var store = new AiEntitlementStore(application);
        using IDisposable existingSubscription = application.AuthenticatedUser.Subscribe(_ => { });
        var snapshots = new List<AiEntitlements?>();
        using IDisposable entitlementSubscription = store.Entitlements.Subscribe(snapshot =>
        {
            snapshots.Add(snapshot);
            if (snapshot is null)
                return;

            entitlementCallbackEntered.Set();
            if (!authenticationCallbackEntered.Wait(timeout))
                throw new TimeoutException("The authentication notification did not start.");

            if (dispose)
                existingSubscription.Dispose();
            else
            {
                using IDisposable nextSubscription = application.AuthenticatedUser.Subscribe(user =>
                    Assert.That(user, Is.Null));
            }
            subscriptionChanged.Set();
        });
        var entitlement = new AiEntitlements("pro", "active", null, null, false, true,
            new AiBalance(new AiMonthlyUsage(0, 100, false), 1, false), new AiOperationAvailability([]));
        Task publication = Task.Factory.StartNew(
            () => store.ApplyEntitlements(entitlement, owner),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task signOut = Task.Factory.StartNew(() =>
        {
            if (!entitlementCallbackEntered.Wait(timeout))
                throw new TimeoutException("The entitlement notification did not start.");

            application.SignOut(deleteFile: false);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await Task.WhenAll(publication, signOut).WaitAsync(timeout + timeout);
        // An old account's delayed response must not restore the cleared snapshot.
        store.ApplyEntitlements(entitlement, owner);
        Assert.Multiple(() =>
        {
            Assert.That(application.AuthenticatedUser.Value, Is.Null);
            Assert.That(store.Entitlements.Value, Is.Null);
            Assert.That(snapshots, Is.EqualTo(new AiEntitlements?[] { null, entitlement, null }));
        });
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

    [TestCase(false)]
    [TestCase(true)]
    public void SharedState_SubscriptionAfterCompletionDoesNotReplay(bool sourceCompletes)
    {
        using var source = new ReactivePropertySlim<int>(1);
        using var state = new SynchronizedReadOnlyReactiveProperty<int>(source);
        if (sourceCompletes)
            source.Dispose();
        else
            state.Dispose();

        var values = new List<int>();
        int completed = 0;
        using IDisposable subscription = state.Subscribe(values.Add, () => completed++);
        Assert.Multiple(() =>
        {
            Assert.That(values, Is.Empty);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(state.Value, Is.EqualTo(1));
        });
    }
}
