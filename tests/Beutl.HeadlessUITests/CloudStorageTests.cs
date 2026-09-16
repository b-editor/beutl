using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageTests
{
    [AvaloniaTest]
    public async Task SignedOut_ShowsAccountEntryWithoutRequestingStorage()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        using var vm = Create(clients);
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(view.FindControl<StackPanel>("SignInState")!.IsEffectivelyVisible, Is.True);
            Assert.That(view.FindControl<Button>("AccountSettingsButton")!.IsEffectivelyVisible, Is.True);
            Assert.That(((System.Windows.Input.ICommand)vm.Refresh).CanExecute(null), Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(null, "無料")]
    [TestCase("100gb", "100GB")]
    [TestCase("200gb", "200GB")]
    [TestCase("1tb", "1TB")]
    public async Task StoragePlanLabelsFollowTheApiContract(string? plan, string expected)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja");
            using var handler = new Handler { UsagePlan = plan };
            using var http = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
            SignIn(clients, "a");
            using var vm = Create(clients);
            await WaitFor(() => handler.Requests.Count == 1);
            handler.Requests[0].Complete(Response());
            await WaitFor(() => !vm.IsLoading.Value);
            Assert.That(vm.UsageText.Value, Does.EndWith(expected));
        }
        finally { CultureInfo.CurrentUICulture = originalCulture; }
    }

    [AvaloniaTest]
    public async Task UsesAuthenticatedApiAndNavigatesFoldersByKeyboard()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        Assert.That(handler.Requests[0].Authorization, Is.EqualTo("Bearer token-a"));
        Assert.That(handler.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/entries"));
        handler.Requests[0].Complete(Response());
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "folder & 日本", "file" }));
        Assert.That(vm.UsagePercent.Value, Is.EqualTo(50));
        Assert.That(vm.UsageText.Value, Does.Contain("5 GiB"));

        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            list.SelectedIndex = 0;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitFor(() => handler.Requests.Count == 2);
            Assert.That(Uri.UnescapeDataString(handler.Requests[1].Uri.Query), Does.Contain("parentId=folder & 日本"));
            handler.Requests[1].Complete(Response(folder: "folder & 日本"));
            await WaitFor(() => !vm.IsLoading.Value);
            Assert.That(vm.Breadcrumbs.Last().FolderId, Is.EqualTo("folder & 日本"));
            Assert.That(vm.Breadcrumbs.Last().Name, Is.EqualTo("素材"));
            Assert.That(vm.Items.Count, Is.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task IncrementalLoadingKeepsTheFolderAndStopsAtTheEnd()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response());
        await WaitFor(() => !vm.IsLoading.Value);

        var navigation = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => handler.Requests.Count == 2);
        Assert.That(Uri.UnescapeDataString(handler.Requests[1].Uri.Query), Does.Contain("parentId=folder & 日本"));
        handler.Requests[1].Complete(Response(folder: "folder & 日本", pageCount: 2));
        await navigation;
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.Items.All(x => !x.IsFolder), Is.True);
        Assert.That(vm.HasMore.Value, Is.True);
        var append = vm.LoadMoreAsync();
        await WaitFor(() => handler.Requests.Count == 3);
        Assert.That(handler.Requests[2].Uri.Query, Does.Contain("cursor=cursor-2"));
        Assert.That(Uri.UnescapeDataString(handler.Requests[2].Uri.Query), Does.Contain("parentId=folder & 日本"));
        handler.Requests[2].Complete(Response(folder: "folder & 日本", page: 2, pageCount: 2, fileId: "second"));
        await append;
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.HasMore.Value, Is.False);
        Assert.That(vm.Items.Select(x => x.Id), Is.EqualTo(new[] { "file", "second" }));
    }

    [AvaloniaTest]
    public async Task AccountSwitchAndSignOutDiscardDelayedResponsesAndClearUsage()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        SignIn(clients, "b");
        await WaitFor(() => handler.Requests.Count == 2);
        Assert.That(handler.Requests[0].Token.IsCancellationRequested, Is.True);
        handler.Requests[1].Complete(Response(name: "account-b"));
        await WaitFor(() => !vm.IsLoading.Value);
        handler.Requests[0].Complete(Response(name: "account-a"));
        await Drain();
        Assert.That(vm.Items.Last().Name, Is.EqualTo("account-b"));
        var refresh = vm.LoadAsync();
        await WaitFor(() => handler.Requests.Count == 3);
        SignIn(clients, null);
        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.UsageText.Value, Is.Empty);
        Assert.That(vm.HasUsage.Value, Is.False);
        Assert.That(vm.SignedIn.Value, Is.False);
        Assert.That(vm.IsLoading.Value, Is.False);
        Assert.That(handler.Requests[2].Token.IsCancellationRequested, Is.True);
        handler.Requests[2].Complete(Response(name: "late-account-b"));
        await refresh;
        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.Error.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task BackgroundSignOutBeforeUiCommitCannotRestoreThePreviousAccount()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response(name: "old-account"));
        // Keep the UI occupied while the response and account notification queue up.
        // Neither is allowed to publish the old account once the UI resumes.
        SignInOnBackgroundThread(clients, null);
        await WaitFor(() => !vm.SignedIn.Value);
        await Drain();
        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.HasUsage.Value, Is.False);
        Assert.That(vm.IsLoading.Value, Is.False);
        Assert.That(vm.Error.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task NewerRefreshWinsAndDisposalCancelsPendingWork()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        var newer = vm.LoadAsync();
        await WaitFor(() => handler.Requests.Count == 2);
        handler.Requests[1].Complete(Response(name: "newer"));
        await newer;
        handler.Requests[0].Complete(Response(name: "older"));
        await Drain();
        Assert.That(vm.Items.Last().Name, Is.EqualTo("newer"));
        var pending = vm.LoadAsync();
        await WaitFor(() => handler.Requests.Count == 3);
        vm.Dispose();
        Assert.That(handler.Requests[2].Token.IsCancellationRequested, Is.True);
        handler.Requests[2].Complete(Response());
        await pending;
        Assert.That(vm.Items, Is.Empty);
    }

    [AvaloniaTest]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task FailedLoadHasRetryAndDoesNotAppearEmpty(HttpStatusCode status)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete("{}", status);
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.Error.Value, Is.EqualTo(status == HttpStatusCode.NotFound
            ? Strings.CloudStorageUnavailable : Strings.CloudStorageLoadFailed));
        Assert.That(vm.IsEmpty.Value, Is.False);
        vm.Refresh.Execute();
        await WaitFor(() => handler.Requests.Count == 2);
        handler.Requests[1].Complete(Response(empty: true));
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.Error.Value, Is.Null);
        Assert.That(vm.IsEmpty.Value, Is.True);
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task LayoutKeepsNavigationVisibleAtNarrowWidths(int width, bool light)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SignIn(clients, "a");
        using var vm = Create(clients);
        await WaitFor(() => handler.Requests.Count == 1);
        handler.Requests[0].Complete(Response(name: "非常に長い日本語の映像素材のファイル名です-September-2026.mp4"));
        await WaitFor(() => !vm.IsLoading.Value);
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 520,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            foreach (string name in new[] { "StorageItems", "UsageText" })
            {
                var control = view.FindControl<Control>(name)!;
                var position = control.TranslatePoint(default, view)!.Value;
                Assert.That(control.IsEffectivelyVisible, Is.True, name);
                Assert.That(position.X, Is.GreaterThanOrEqualTo(0), name);
                Assert.That(position.X + control.Bounds.Width, Is.LessThanOrEqualTo(width), name);
                Assert.That(position.Y + control.Bounds.Height, Is.LessThanOrEqualTo(520), name);
            }
            Assert.That(view.FindControl<ListBox>("StorageItems")!.Bounds.Height, Is.GreaterThan(120));
            var label = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(x => x.IsEffectivelyVisible && x.Text?.StartsWith("非常に長い") == true);
            Assert.That(label.TextLayout.TextLines, Has.Count.EqualTo(2), "Long tile names must retain both lines.");
            var tile = label.FindAncestorOfType<ListBoxItem>()!;
            var labelPosition = label.TranslatePoint(default, tile)!.Value;
            Assert.That(labelPosition.Y + label.Bounds.Height, Is.LessThanOrEqualTo(tile.Bounds.Height));
            if (Environment.GetEnvironmentVariable("BEUTL_STORAGE_CAPTURE") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(path);
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(path, $"cloud-storage-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FileBrowserIsAvailableWithoutASeparateStorageTab()
    {
        await TestReset.ResetShellAsync();
        Assert.That(TestShell.MainViewModel.ToolTabExtensions, Does.Contain(FileBrowserTabExtension.Instance));
        Assert.That(TestShell.MainViewModel.ToolTabExtensions.Any(x => x.Name == "CloudStorage"), Is.False);
    }

    private static CloudStorageViewModel Create(BeutlApiApplication clients) =>
        new(clients, () => throw new InvalidOperationException("Not used by this test"));

    internal static void SignInOnBackgroundThread(BeutlApiApplication clients, string? id)
    {
        // A synchronous wait can inline a queued Task.Run on the headless UI thread.
        // Use a dedicated thread so its UI notification stays queued until this call returns.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { SignIn(clients, id); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };
        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(failure, Is.Null);
    }

    internal static void SignIn(BeutlApiApplication clients, string? id)
    {
        var state = (ReactivePropertySlim<AuthenticatedUser?>)typeof(BeutlApiApplication)
            .GetField("_authenticatedUser", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(clients)!;
        state.Value = id == null ? null : new AuthenticatedUser(new Profile(new ProfileResponse
        {
            Id = id,
            Name = id,
            DisplayName = id,
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, clients), new AuthResponse
        {
            Token = $"token-{id}",
            RefreshToken = $"refresh-{id}",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, clients, DateTime.UtcNow);
    }

    internal static string Response(string name = "video.mp4", string? folder = null, int page = 1, int pageCount = 1, bool empty = false, string fileId = "file", int fileCount = 1, string visibility = "PRIVATE")
        => JsonSerializer.Serialize(new
        {
            entries = (empty || folder != null || page != 1 ? [] : new[]
            {
                new { id = "folder & 日本", kind = "folder", name = "素材", parentId = (string?)null,
                    size = 0L, mimeType = "", visibility = "", createdAt = "2026-09-01T00:00:00Z",
                    actions = new[] { "open", "rename", "move", "delete" } }
            }).Concat(Enumerable.Range(0, empty ? 0 : fileCount).Select(index => new
            {
                id = fileCount == 1 ? fileId : $"{fileId}-{page}-{index}",
                kind = "file",
                name = fileCount == 1 ? name : $"{index:D2}-{name}",
                parentId = folder,
                size = 5L * 1024 * 1024 * 1024,
                mimeType = "video/mp4",
                visibility,
                createdAt = "2026-09-01T00:00:00Z",
                actions = visibility == "DEDICATED" ? new[] { "open", "download", "move", "details" }
                    : visibility == "PUBLIC" ? new[] { "open", "download", "copyLink", "rename", "move", "details", "setPrivate", "delete" }
                    : new[] { "open", "download", "rename", "move", "details", "setPublic", "delete" },
            })).ToArray(),
            path = folder == null ? [] : new[] { new { id = folder, name = "素材", parentId = (string?)null } },
            parentId = folder,
            nextCursor = page < pageCount ? $"cursor-{page + 1}" : null,
        });

    internal static string UsageResponse(string? plan = null) => JsonSerializer.Serialize(new
    {
        plan,
        quotaBytes = 10L * 1024 * 1024 * 1024,
        usedBytes = 5L * 1024 * 1024 * 1024,
        fileCount = 1,
        fileCountLimit = 10000,
    });

    internal static async Task WaitFor(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) { HeadlessTestHelpers.Settle(); await Task.Delay(10, timeout.Token); }
    }

    private static async Task Drain() { await Task.Delay(50); HeadlessTestHelpers.Settle(); }

    internal sealed class Pending(HttpRequestMessage request, CancellationToken token)
    {
        public Uri Uri { get; } = request.RequestUri!;
        public HttpMethod Method { get; } = request.Method;
        public Task<string> ReadBodyAsync() => request.Content?.ReadAsStringAsync() ?? Task.FromResult("");
        public string Authorization { get; } = request.Headers.Authorization?.ToString() ?? "";
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<HttpResponseMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(string json, HttpStatusCode status = HttpStatusCode.OK) => Completion.SetResult(new(status)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    // Deliberately ignores cancellation so stale-response guards are exercised too.
    internal sealed class Handler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Pending> _requests = new();
        public string? UsagePlan { get; set; }
        public ConcurrentQueue<string> UsageAuthorizations { get; } = new();
        public IReadOnlyList<Pending> Requests => _requests.ToArray();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/storage/usage", StringComparison.Ordinal))
            {
                UsageAuthorizations.Enqueue(request.Headers.Authorization?.ToString() ?? "");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(UsageResponse(UsagePlan), Encoding.UTF8, "application/json"),
                });
            }
            var pending = new Pending(request, cancellationToken);
            _requests.Enqueue(pending);
            return pending.Completion.Task;
        }
    }
}
