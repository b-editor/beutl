using System.Collections.Concurrent;
using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserAdBlockFilterConcurrencyTests
{
    private const string OriginalUrl = "https://filters.example.test/original.txt";
    private const string SelectedUrl = "https://filters.example.test/selected.txt";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcurrentGetKeepsEachTaskAssociatedWithItsSources(bool sameSources)
    {
        string root = NewRoot();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var response = Completion<HttpResponseMessage>();
        int requests = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                entered.Set();
                if (!release.Wait(Timeout)) throw new TimeoutException("The first GetAsync call was not released.");
                return response.Task;
            }
            return Task.FromResult(Response(request.RequestUri!.AbsoluteUri == SelectedUrl ? "selected" : "original"));
        }));
        var store = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        Task<BrowserAdBlockRules>? first = null, second = null;
        Task caller = Task.Run(() => { first = store.GetAsync([OriginalUrl]); });
        try
        {
            Assert.That(entered.Wait(Timeout), Is.True);
            string source = sameSources ? OriginalUrl : SelectedUrl;
            second = store.GetAsync([source]);
            release.Set();
            await caller.WaitAsync(Timeout);
            Assert.That(store.GetAsync([source]), Is.SameAs(second), "A late first call must not replace the second call's task.");
            if (sameSources) Assert.That(first, Is.SameAs(second));
            response.SetResult(Response("original"));
            BrowserAdBlockRules current = await second.WaitAsync(Timeout);
            if (!sameSources) Assert.CatchAsync<OperationCanceledException>(async () => await first!.WaitAsync(Timeout));
            Assert.That(store.Current, Is.SameAs(current));
            Assert.That(requests, Is.EqualTo(sameSources ? 1 : 2));
        }
        finally
        {
            release.Set();
            if (!response.Task.IsCompleted) response.SetResult(Response("original"));
            await caller.WaitAsync(Timeout);
            await DrainAsync(first, second);
            DeleteRoot(root);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task LateCacheParsingCannotReplaceANewerUpdateOrStartAStaleDownload(bool sameSources, bool failParsing)
    {
        string root = NewRoot();
        string path = Path.Combine(root, "filters.json");
        string responseVersion = "original";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(responseVersion));
        }));
        await new BrowserAdBlockFilterStore(path, client).UpdateAsync([OriginalUrl], CancellationToken.None);
        var entered = Completion<bool>();
        var release = Completion<bool>();
        var store = new BrowserAdBlockFilterStore(path, client, async (text, _) =>
        {
            if (text.Contains("original-ad.test", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                await release.Task;
                if (failParsing) throw new InvalidDataException("Invalid old cache.");
            }
            return BrowserAdBlockRules.Parse(text);
        });
        var published = new ConcurrentQueue<BrowserAdBlockRules?>();
        store.Changed += () => published.Enqueue(store.Current);
        Task<BrowserAdBlockRules> old = store.GetAsync([OriginalUrl]);
        try
        {
            await entered.Task.WaitAsync(Timeout);
            responseVersion = "selected";
            string selectedUrl = sameSources ? OriginalUrl : SelectedUrl;
            BrowserAdBlockRules latest = await store.UpdateAsync([selectedUrl], CancellationToken.None);
            string saved = await File.ReadAllTextAsync(path);
            DateTimeOffset? updated = store.UpdatedAt;
            release.SetResult(true);
            await DrainAsync(old);
            Assert.Multiple(() =>
            {
                Assert.That(old.IsCanceled, Is.True);
                Assert.That(store.Current, Is.SameAs(latest));
                Assert.That(store.UpdatedAt, Is.EqualTo(updated));
                Assert.That(published, Is.EqualTo(new[] { latest }));
                Assert.That(requests, Is.EqualTo(new[] { OriginalUrl, selectedUrl }));
            });
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(saved));
            Assert.That(await store.GetAsync([selectedUrl]), Is.SameAs(latest));
        }
        finally { release.TrySetResult(true); await DrainAsync(old); DeleteRoot(root); }
    }

    [Test]
    public async Task SupersededDownloadCannotPublishWhileTheSelectedDownloadIsPending()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "filters.json");
        var originalEntered = Completion<bool>();
        var selectedEntered = Completion<bool>();
        var originalResponse = Completion<HttpResponseMessage>();
        var selectedResponse = Completion<HttpResponseMessage>();
        bool seed = true;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (seed) return Task.FromResult(Response("seed"));
            if (request.RequestUri!.AbsoluteUri == OriginalUrl)
            {
                originalEntered.TrySetResult(true);
                return originalResponse.Task;
            }
            selectedEntered.TrySetResult(true);
            return selectedResponse.Task;
        }));
        var store = new BrowserAdBlockFilterStore(path, client);
        BrowserAdBlockRules cached = await store.UpdateAsync([OriginalUrl], CancellationToken.None);
        string saved = await File.ReadAllTextAsync(path);
        var published = new ConcurrentQueue<BrowserAdBlockRules?>();
        store.Changed += () => published.Enqueue(store.Current);
        seed = false;
        Task<BrowserAdBlockRules> old = store.UpdateAsync([OriginalUrl], CancellationToken.None);
        Task<BrowserAdBlockRules>? latest = null;
        try
        {
            await originalEntered.Task.WaitAsync(Timeout);
            latest = store.UpdateAsync([SelectedUrl], CancellationToken.None);
            originalResponse.SetResult(Response("original"));
            await selectedEntered.Task.WaitAsync(Timeout);
            await DrainAsync(old);
            Assert.Multiple(() =>
            {
                Assert.That(old.IsCanceled, Is.True);
                Assert.That(store.Current, Is.SameAs(cached));
                Assert.That(published, Is.Empty);
            });
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(saved));
            selectedResponse.SetResult(Response("selected"));
            BrowserAdBlockRules selected = await latest.WaitAsync(Timeout);
            Assert.That(store.Current, Is.SameAs(selected));
            Assert.That(published, Is.EqualTo(new[] { selected }));
        }
        finally
        {
            if (!originalResponse.Task.IsCompleted) originalResponse.SetResult(Response("original"));
            if (!selectedResponse.Task.IsCompleted) selectedResponse.SetResult(Response("selected"));
            await DrainAsync(old, latest);
            DeleteRoot(root);
        }
    }

    [AvaloniaTest]
    public async Task SessionWaitsForSelectedFiltersWhenItsCacheLoadIsSuperseded()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "filters.json");
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
            Response(request.RequestUri!.AbsoluteUri == OriginalUrl ? "original" : "selected"))));
        await new BrowserAdBlockFilterStore(path, client).UpdateAsync([OriginalUrl], CancellationToken.None);
        var entered = Completion<bool>();
        var release = Completion<bool>();
        var store = new BrowserAdBlockFilterStore(path, client, async (text, _) =>
        {
            if (text.Contains("original-ad.test", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                await release.Task;
            }
            return BrowserAdBlockRules.Parse(text);
        });
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: store);
        profile.UpdateAdBlockListUrls([OriginalUrl]);
        profile.UpdateSettings(BrowserSearchEngine.Google, false, false, true);
        var ready = Completion<BrowserAdBlockRules>();
        var errors = new List<string>();
        var webView = new NativeWebView();
        var destination = new Uri("https://publisher.test/page");
        using var session = new BrowserAdBlockSession(webView, profile, destination, errors.Add,
            (_, rules) => Task.FromResult<IBrowserAdBlockBackend>(new Backend(() => ready.TrySetResult(rules))));
        Task<BrowserAdBlockRules> old = store.GetAsync([OriginalUrl]);
        try
        {
            session.Attach(new PlatformHandle((nint)1, "test"));
            await entered.Task.WaitAsync(Timeout);
            profile.UpdateAdBlockListUrls([SelectedUrl]);
            BrowserAdBlockRules latest = await store.UpdateAsync([SelectedUrl], CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            Assert.That(session.IsPreparing, Is.True);
            Assert.That(webView.Source.Scheme, Is.EqualTo("about"));
            release.SetResult(true);
            Assert.That(await ready.Task.WaitAsync(Timeout), Is.SameAs(latest));
            Assert.That(errors, Is.Empty);
            Assert.That(webView.Source, Is.EqualTo(destination));
        }
        finally { release.TrySetResult(true); await DrainAsync(old); DeleteRoot(root); }
    }

    [AvaloniaTest]
    public async Task SessionDiscardsABackendPreparedForSupersededSources()
    {
        string root = NewRoot();
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
            Response(request.RequestUri!.AbsoluteUri == OriginalUrl ? "original" : "selected"))));
        var store = new BrowserAdBlockFilterStore(Path.Combine(root, "filters.json"), client);
        BrowserAdBlockRules original = await store.UpdateAsync([OriginalUrl], CancellationToken.None);
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: store);
        profile.UpdateAdBlockListUrls([OriginalUrl]);
        profile.UpdateSettings(BrowserSearchEngine.Google, false, false, true);
        var pending = Completion<IBrowserAdBlockBackend>();
        var prepared = Completion<bool>();
        var ready = Completion<BrowserAdBlockRules>();
        var errors = new List<string>();
        var oldBackend = new Backend(() => { });
        var webView = new NativeWebView();
        var destination = new Uri("https://publisher.test/page");
        using var session = new BrowserAdBlockSession(webView, profile, destination, errors.Add, (_, rules) =>
        {
            if (ReferenceEquals(rules, original))
            {
                prepared.TrySetResult(true);
                return pending.Task;
            }
            return Task.FromResult<IBrowserAdBlockBackend>(new Backend(() => ready.TrySetResult(rules)));
        });
        try
        {
            session.Attach(new PlatformHandle((nint)1, "test"));
            await prepared.Task.WaitAsync(Timeout);
            profile.UpdateAdBlockListUrls([SelectedUrl]);
            BrowserAdBlockRules latest = await store.UpdateAsync([SelectedUrl], CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            pending.SetResult(oldBackend);
            Assert.That(await ready.Task.WaitAsync(Timeout), Is.SameAs(latest));
            Assert.Multiple(() =>
            {
                Assert.That(oldBackend.Enabled, Is.False);
                Assert.That(oldBackend.Disposed, Is.True);
                Assert.That(errors, Is.Empty);
                Assert.That(webView.Source, Is.EqualTo(destination));
            });
        }
        finally { pending.TrySetResult(oldBackend); DeleteRoot(root); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task SettingsDoNotReportSupersededRequestsAsDownloadFailures(bool loadCache)
    {
        string root = NewRoot();
        string path = Path.Combine(root, "filters.json");
        var entered = Completion<bool>();
        var release = Completion<bool>();
        bool seed = true;
        using var client = new HttpClient(new Handler(async (request, _) =>
        {
            if (!seed && !loadCache && request.RequestUri!.AbsoluteUri == OriginalUrl)
            {
                entered.TrySetResult(true);
                await release.Task;
            }
            return Response(request.RequestUri!.AbsoluteUri == OriginalUrl ? "original" : "selected");
        }));
        if (loadCache) await new BrowserAdBlockFilterStore(path, client).UpdateAsync([OriginalUrl], CancellationToken.None);
        seed = false;
        var store = new BrowserAdBlockFilterStore(path, client, async (text, _) =>
        {
            if (loadCache && text.Contains("original-ad.test", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                await release.Task;
            }
            return BrowserAdBlockRules.Parse(text);
        });
        var profile = new BrowserProfile(Path.Combine(root, "profile.json"), adBlockFilters: store);
        profile.UpdateAdBlockListUrls([OriginalUrl]);
        profile.UpdateSettings(BrowserSearchEngine.Google, false, false, loadCache);
        using var vm = new BrowserSettingsPageViewModel(profile, () => null);
        Task old = loadCache ? store.GetAsync([OriginalUrl]) : vm.UpdateFiltersAsync();
        Task<BrowserAdBlockRules>? latest = null;
        try
        {
            await entered.Task.WaitAsync(Timeout);
            profile.UpdateAdBlockListUrls([SelectedUrl]);
            latest = store.UpdateAsync([SelectedUrl], CancellationToken.None);
            release.SetResult(true);
            await latest.WaitAsync(Timeout);
            await DrainAsync(old);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.FilterFeedback.Value, Is.Null);
            Assert.That(vm.IsUpdatingFilters.Value, Is.False);
        }
        finally { release.TrySetResult(true); await DrainAsync(old, latest); DeleteRoot(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private static void DeleteRoot(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Response(string version) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"[Adblock Plus 2.0]\n||{version}-ad.test^")
    };

    private static async Task DrainAsync(params Task?[] tasks)
    {
        foreach (Task? task in tasks)
        {
            if (task == null) continue;
            try { await task.WaitAsync(Timeout); }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class Backend(Action enable) : IBrowserAdBlockBackend
    {
        internal bool Enabled, Disposed;
        public Task EnableAsync() { Enabled = true; enable(); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}
