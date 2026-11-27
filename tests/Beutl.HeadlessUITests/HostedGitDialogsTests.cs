using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.Views;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class HostedGitDialogsTests
{
    [AvaloniaTest]
    [TestCase("ja-JP", true)]
    [TestCase("ja-JP", false)]
    [TestCase("en-US", true)]
    [TestCase("en-US", false)]
    public async Task Repository_result_actions_show_the_full_label_in_a_narrow_window(string culture, bool hasLocalCopy)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var dialog = new RepositoryCloneResultDialog
        {
            Title = hasLocalCopy ? Strings.HostedGit_ExistingCloneFound : Strings.HostedGit_CloneRepository,
            PrimaryButtonText = hasLocalCopy ? Strings.HostedGit_OpenLocalProject : Strings.CreateNew,
            SecondaryButtonText = hasLocalCopy ? Strings.HostedGit_CloneNewCopy : Strings.HostedGit_OpenFolder,
            CloseButtonText = hasLocalCopy ? Strings.Cancel : Strings.Close,
            DefaultButton = FAContentDialogButton.Primary,
        };
        var window = new Window { Width = 320, Height = 560 };
        window.Show();
        Task<FAContentDialogResult> displayed = dialog.ShowAsync(window);
        try
        {
            HeadlessTestHelpers.Render(4);
            await Task.Delay(300);
            HeadlessTestHelpers.Render(4);
            foreach (string action in new[] { dialog.PrimaryButtonText, dialog.SecondaryButtonText, dialog.CloseButtonText })
            {
                var button = dialog.GetVisualDescendants().OfType<Button>().Single(item => Equals(item.Content, action));
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == action);
                var labelEnd = label.TranslatePoint(new Point(label.TextLayout.Width, label.TextLayout.Height), button)!.Value;
                var buttonEnd = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), window)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(label.IsEffectivelyVisible, Is.True, action);
                    Assert.That(label.TextLayout.TextLines.Any(line => line.HasCollapsed), Is.False, action);
                    Assert.That(labelEnd.X, Is.LessThanOrEqualTo(button.Bounds.Width + 1), action);
                    Assert.That(labelEnd.Y, Is.LessThanOrEqualTo(button.Bounds.Height + 1), action);
                    Assert.That(button.TranslatePoint(default, window)!.Value.X, Is.GreaterThanOrEqualTo(-1), action);
                    Assert.That(buttonEnd.X, Is.LessThanOrEqualTo(window.ClientSize.Width + 1), action);
                    Assert.That(buttonEnd.Y, Is.LessThanOrEqualTo(window.ClientSize.Height + 1), action);
                });
            }
        }
        finally
        {
            dialog.Hide();
            await displayed;
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    public async Task Repository_result_dialog_closes_when_its_operation_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        var window = new Window { Width = 480, Height = 560 };
        var dialog = new RepositoryCloneResultDialog { Title = "Choose a project", PrimaryButtonText = "Open" };
        window.Show();
        Task<FAContentDialogResult> result = RepositoryCloneWorkflow.ShowDialogAsync(dialog, window, cancellation.Token);
        try
        {
            HeadlessTestHelpers.Render(4);
            Assert.That(dialog.IsAttachedToVisualTree(), Is.True);
            cancellation.Cancel();
            await HeadlessTestHelpers.WaitUntilAsync(() => result.IsCompleted);
            Assert.That(result.IsCanceled, Is.True);
        }
        finally
        {
            if (!result.IsCompleted)
                dialog.Hide();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Clone_dialog_reselects_a_linked_repository_and_shows_the_revision_in_Japanese()
    {
        const string id = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
        const string revision = "作品/main";
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        var config = new ViewConfig();
        var handler = new RepositoryListHandler(id) { Unavailable = true };
        using var http = new HttpClient(handler);
        await using var api = new BeutlApiApplication(http, new ExtensionProvider(new ExtensionConfig()),
            config, new UpdateConfig(), static () => new HttpClient(), persistAuthenticatedUser: _ => { });
        var request = new RepositoryOpenRequest(Guid.Parse(id), revision);
        using var vm = new CloneRepositoryDialogViewModel(api, config,
            new GitRepositoryCloner(new VersionControlConfig()), request, CancellationToken.None);
        var dialog = new CloneRepositoryDialog { DataContext = vm };
        var window = new Window { Width = 480, Height = 560, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Task<FAContentDialogResult> displayed = dialog.ShowAsync(window);
        try
        {
            Assert.That(vm.Picker.NeedsSignIn.Value, Is.True);
            Assert.That(vm.CanClone.Value, Is.False);
            await api.RunInteractiveAuthenticationAsync(_ => Task.FromResult(CreateUser(api)), CancellationToken.None);
            await HeadlessTestHelpers.WaitUntilAsync(() => !string.IsNullOrEmpty(vm.Picker.Status.Value));
            string requestError = vm.Picker.Status.Value;
            vm.Picker.Search.Value = "制作";
            Assert.That(vm.Picker.Status.Value, Is.EqualTo(requestError));
            handler.Unavailable = false;
            vm.Picker.Search.Value = "";
            await vm.Picker.RefreshAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => vm.Picker.SelectedRepository.Value?.Id == id,
                TimeSpan.FromSeconds(10), "The repository link was not preselected after sign-in.");
            await vm.CheckGitAsync();
            HeadlessTestHelpers.Render(4);

            Assert.That(vm.Revision.Value, Is.EqualTo(revision));
            Assert.That(vm.FolderName.Value, Is.EqualTo("制作プロジェクト"));
            Assert.That(dialog.Title, Is.EqualTo(Strings.HostedGit_CloneRepository));
            Assert.That(dialog.GetVisualDescendants().OfType<TextBlock>().Any(block =>
                block.IsEffectivelyVisible && block.Text == string.Format(Strings.HostedGit_RevisionFormat, revision)), Is.True);
            if (Environment.GetEnvironmentVariable("BEUTL_THEME_CAPTURE_DIR") is not null)
            {
                vm.ParentFolder.Value = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Projects");
                HeadlessTestHelpers.Render(4);
                ThemeCaptureTests.Capture(window, "clone-repository-ja.png");
            }

            vm.Picker.Search.Value = "別の";
            Assert.That(vm.Picker.Repositories.Value.Select(repo => repo.Name), Is.EqualTo(new[] { "別のリポジトリ" }));
            Assert.That(vm.Picker.SelectedRepository.Value, Is.Null);
            vm.Picker.Search.Value = "";
            Assert.That(vm.Picker.SelectedRepository.Value?.Id, Is.EqualTo(id));

            HeadlessTestHelpers.Click(dialog.GetVisualDescendants().OfType<TabStripItem>().Single(item => item.Name == "UrlSource"));
            vm.Url.Value = "https://example.invalid/story/sample.git";
            Assert.That(vm.Revision.Value, Is.Null);
            Assert.That(vm.FolderName.Value, Is.EqualTo("sample"));
        }
        finally
        {
            dialog.Hide();
            await displayed;
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    [TestCase(HttpStatusCode.NoContent)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task Deleting_a_repository_removes_it_from_the_loaded_list(HttpStatusCode outcome)
    {
        const string id = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
        var handler = new RepositoryListHandler(id) { DeleteStatus = outcome };
        using var http = new HttpClient(handler);
        await using var api = new BeutlApiApplication(http, new ExtensionProvider(new ExtensionConfig()),
            new ViewConfig(), new UpdateConfig(), static () => new HttpClient(), persistAuthenticatedUser: _ => { });
        AuthenticatedUser user = await api.RunInteractiveAuthenticationAsync(
            _ => Task.FromResult(CreateUser(api)), CancellationToken.None);
        using var picker = new HostedRepositoryPickerViewModel(api, CancellationToken.None, Guid.Parse(id));
        await picker.RefreshAsync();
        Assert.That(picker.SelectedRepository.Value?.Id, Is.EqualTo(id));

        await picker.DeleteAsync(Guid.Parse(id), user);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Repositories.Value.Select(repo => repo.Name), Is.EqualTo(new[] { "別のリポジトリ" }));
            Assert.That(picker.SelectedRepository.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    [TestCase("ja-JP", false)]
    [TestCase("en-US", true)]
    public async Task Repository_deletion_shows_progress_until_the_request_finishes(string culture, bool fails)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window { Width = 440, Height = 480, RequestedThemeVariant = ThemeVariant.Light };
        int requests = 0;
        window.Show();
        Task shown = EditorHostFallback.ShowHostedRepositoryDeletionDialogAsync(window, "Demo", () =>
        {
            requests++;
            return request.Task;
        });
        try
        {
            HeadlessTestHelpers.Render(4);
            await Task.Delay(300);
            HeadlessTestHelpers.Render(4);
            var dialog = window.GetVisualDescendants().OfType<FAContentDialog>().Single();
            var ring = dialog.GetVisualDescendants().OfType<FAProgressRing>().Single();
            var delete = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PrimaryButton");
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CloseButton");
            Assert.That(ring.IsEffectivelyVisible, Is.False);

            HeadlessTestHelpers.Click(delete, move: true, ticks: 5);
            Assert.Multiple(() =>
            {
                Assert.That(requests, Is.EqualTo(1));
                Assert.That(shown.IsCompleted, Is.False);
                Assert.That(ring.IsEffectivelyVisible && ring.IsActive, Is.True);
                Assert.That(delete.IsEffectivelyEnabled, Is.False);
                Assert.That(cancel.IsEffectivelyEnabled, Is.False);
            });
            if (HeadlessCapture.IsRequested)
            {
                await Task.Delay(550);
                HeadlessTestHelpers.Render(4);
                HeadlessCapture.SaveIfRequested(ring, nameof(HostedGitDialogsTests), $"repository-deleting-{culture}");
            }

            if (fails)
            {
                request.SetException(new IOException("The deletion request failed."));
                await Assert.ThrowsAsync<IOException>(() => shown.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                request.SetResult();
                await shown.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.That(dialog.IsAttachedToVisualTree(), Is.False);
        }
        finally
        {
            request.TrySetResult();
            foreach (var dialog in window.GetVisualDescendants().OfType<FAContentDialog>().ToArray())
                dialog.Hide();
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
            HeadlessTestHelpers.Settle();
        }
    }

    private static AuthenticatedUser CreateUser(BeutlApiApplication api)
        => new(new Profile(new ProfileResponse
        {
            Id = "example-owner",
            Name = "sample",
            DisplayName = "Sample",
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, api), new AuthResponse
        {
            Token = "example",
            RefreshToken = "example-refresh",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, api, DateTime.UtcNow);

    private sealed class RepositoryListHandler(string id) : HttpMessageHandler
    {
        public bool Unavailable { get; set; }
        public HttpStatusCode DeleteStatus { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Assert.That(request.RequestUri?.AbsolutePath, Is.EqualTo($"/api/v3/repos/{id}"));
                return Task.FromResult(new HttpResponseMessage(DeleteStatus) { RequestMessage = request });
            }

            Assert.That(request.RequestUri?.AbsolutePath, Is.EqualTo("/api/v3/repos"));
            if (Unavailable)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            DateTimeOffset date = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
            GitRepositorySummary first = new(id, "制作プロジェクト", $"https://example.invalid/api/v3/git/{id}.git", date, date);
            GitRepositorySummary second = new("76972d37-e697-401c-8291-6d08cdeb4d81", "別のリポジトリ", "", date, date);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new GitRepositoryListResponse([second, first])),
            });
        }
    }
}
