using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// A project's picture on the start page: what its close keeps, where, and when it keeps nothing.
[TestFixture, NonParallelizable]
public class ProjectThumbnailTests
{
    private static readonly Color s_blue = Color.FromRgb(0x20, 0x40, 0xF0);
    private static readonly Color s_red = Color.FromRgb(0xF0, 0x20, 0x20);

    private static Func<EngineObject> FullFrame(Color color, int width, int height) => () =>
    {
        var rect = new RectShape();
        rect.Width.Value = width;
        rect.Height.Value = height;
        rect.Fill.Value = new SolidColorBrush(color);
        return rect;
    };

    private static (byte R, byte G, byte B, byte A) CenterPixel(Bitmap bitmap)
    {
        using Bitmap converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        ReadOnlySpan<byte> row = converted.GetRow(converted.Height / 2);
        ReadOnlySpan<byte> pixel = row.Slice(converted.Width / 2 * converted.BytesPerPixel, 4);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }

    [AvaloniaTest]
    public async Task A_close_keeps_the_frame_at_the_playhead()
    {
        TestGpu.RequireAnyDevice();
        (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "thumbnail-playhead", duration: MediaTime.FromSeconds(4));
        await TestShell.AddElementAsync(editor, FullFrame(s_blue, 640, 480), MediaTime.Zero, MediaTime.FromSeconds(2));
        await TestShell.AddElementAsync(editor, FullFrame(s_red, 640, 480), MediaTime.FromSeconds(2), MediaTime.FromSeconds(2));
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(3));
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        using Bitmap picture = Bitmap.FromFile(ProjectThumbnailService.GetPath(projectFile));
        (byte r, byte g, byte b, byte a) = CenterPixel(picture);
        Assert.Multiple(() =>
        {
            Assert.That(r, Is.GreaterThan(200), "The red element is at the playhead, and the blue one is not.");
            Assert.That(b, Is.LessThan(100));
            Assert.That(a, Is.EqualTo(255));
            Assert.That(picture.Width, Is.EqualTo(360), "A 4:3 frame is scaled to fit 480x270.");
            Assert.That(picture.Height, Is.EqualTo(270));
        });
    }

    // Playback that runs to the end stops past the last element, which would otherwise leave no picture.
    [AvaloniaTest]
    public async Task A_playhead_past_the_elements_shows_the_middle_of_them_instead()
    {
        TestGpu.RequireAnyDevice();
        (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "thumbnail-past-end", duration: MediaTime.FromSeconds(10));
        await TestShell.AddElementAsync(editor, FullFrame(s_blue, 640, 480), MediaTime.Zero, MediaTime.FromSeconds(2));
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(8));
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        using Bitmap picture = Bitmap.FromFile(ProjectThumbnailService.GetPath(projectFile));
        (byte r, _, byte b, _) = CenterPixel(picture);
        Assert.Multiple(() =>
        {
            Assert.That(b, Is.GreaterThan(200));
            Assert.That(r, Is.LessThan(100));
        });
    }

    [AvaloniaTest]
    public async Task The_start_page_shows_the_frame_of_the_project_just_closed()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Width = 1280, Height = 720 };
        try
        {
            window.Show();
            window.Content = view;
            HeadlessTestHelpers.Render();
            EditorHostFallback fallback = view.GetVisualDescendants().OfType<EditorHostFallback>().Single();
            ListBox list = fallback.FindControl<ListBox>("recentList")!;

            (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
                "thumbnail-start-page", duration: MediaTime.FromSeconds(2), reset: false);
            await TestShell.AddElementAsync(editor, FullFrame(s_blue, 640, 480), MediaTime.Zero, MediaTime.FromSeconds(2));
            HeadlessTestHelpers.Render();
            string projectFile = project.Uri!.LocalPath;
            Assert.That(list.Items, Is.Empty, "The page holds nothing behind the editor.");

            await TestShell.Project.CloseProjectAsync();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => list.Items.Cast<RecentProjectItem>().FirstOrDefault()?.Thumbnail.Value is not null);
            HeadlessTestHelpers.Render();

            ListBoxItem row = list.GetRealizedContainers().OfType<ListBoxItem>()
                .First(item => ((RecentProjectItem)item.DataContext!).FullName == projectFile);
            Assert.Multiple(() =>
            {
                Assert.That(list.Items.Cast<RecentProjectItem>().First().FullName, Is.EqualTo(projectFile));
                Assert.That(row.GetVisualDescendants().OfType<Image>().Single().Source, Is.Not.Null);
            });
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task A_scene_with_nothing_to_show_keeps_the_picture_it_had()
    {
        TestGpu.RequireAnyDevice();
        (Project project, _, _) = await TestShell.OpenNewSceneAsync("thumbnail-empty");
        string projectFile = project.Uri!.LocalPath;
        string picture = ProjectThumbnailService.GetPath(projectFile);

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(File.Exists(picture), Is.False, "An empty frame is no picture of the project.");

        Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
        File.WriteAllText(picture, "earlier");
        await TestShell.Project.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();
        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        Assert.That(File.ReadAllText(picture), Is.EqualTo("earlier"));
    }

    [AvaloniaTest]
    public async Task A_close_that_discards_the_edits_keeps_no_picture_of_them()
    {
        TestGpu.RequireAnyDevice();
        (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("thumbnail-discard");
        await TestShell.AddElementAsync(editor, FullFrame(s_blue, 640, 480), MediaTime.Zero, MediaTime.FromSeconds(2));
        string projectFile = project.Uri!.LocalPath;

        bool closed = await TestShell.Project.TryCloseProjectAsync(project, ProjectService.ProjectCloseIntent.DiscardChanges);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(File.Exists(ProjectThumbnailService.GetPath(projectFile)), Is.False);
        });
    }

    [AvaloniaTest]
    public void A_picture_is_scaled_to_fit_and_lives_with_the_project()
    {
        string file = Path.Combine(TestShell.NewWorkspace("thumbnail-fit"), "fit.bep");
        using var portrait = new Bitmap(1080, 1920);

        ProjectThumbnailService.Save(portrait, file);

        string picture = ProjectThumbnailService.GetPath(file);
        using Bitmap saved = Bitmap.FromFile(picture);
        Assert.Multiple(() =>
        {
            Assert.That(picture, Is.EqualTo(Path.Combine(Path.GetDirectoryName(file)!, ".beutl", "thumbnails", "fit.bep.png")));
            Assert.That(saved.Height, Is.EqualTo(270));
            Assert.That(saved.Width, Is.EqualTo(151));
        });
    }

    [AvaloniaTest]
    public void A_project_file_deleted_on_its_own_takes_its_picture_along()
    {
        string file = Path.Combine(TestShell.NewWorkspace("thumbnail-delete"), "shared.bep");
        File.WriteAllText(file, "{}");
        using (var picture = new Bitmap(32, 18))
        {
            ProjectThumbnailService.Save(picture, file);
        }

        ProjectDiskDeletion.Delete(new ProjectDiskDeletionTarget(file, file, IsFolder: false));

        Assert.That(File.Exists(ProjectThumbnailService.GetPath(file)), Is.False);
    }

    [AvaloniaTest]
    public void A_project_file_without_a_picture_is_deleted_all_the_same()
    {
        string file = Path.Combine(TestShell.NewWorkspace("thumbnail-delete-none"), "shared.bep");
        File.WriteAllText(file, "{}");

        ProjectDiskDeletion.Delete(new ProjectDiskDeletionTarget(file, file, IsFolder: false));

        Assert.That(File.Exists(file), Is.False);
    }

    [AvaloniaTest]
    public async Task Push_syncs_the_thumbnail_to_a_Beutl_hosted_repository_and_skips_non_hosted_remotes()
    {
        TestGpu.RequireAnyDevice();
        const string repoId = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
        const string pushRepoId = "76972d37-e697-401c-8291-6d08cdeb4d81";
        byte[]? uploadedBytes = null;
        var uploadedPaths = new List<string>();

        using var http = new HttpClient(new StubHttpHandler(async request =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(request.Content!.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
            uploadedPaths.Add(request.RequestUri!.AbsolutePath);
            uploadedBytes = await request.Content.ReadAsByteArrayAsync();
            var now = DateTimeOffset.UtcNow;
            var summary = new GitRepositorySummary(
                repoId, "Demo", $"{BeutlApiApplication.BaseUrl}/api/v3/git/{repoId}.git", now, now, now);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(summary) };
        }));
        await using var api = new BeutlApiApplication(
            http, new ExtensionProvider(new ExtensionConfig()), new ViewConfig(), new UpdateConfig(),
            static () => new HttpClient(), persistAuthenticatedUser: _ => { });
        AuthenticatedUser owner = await SignInTestUserAsync(api);

        (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("thumbnail-push-sync");
        await TestShell.AddElementAsync(editor, FullFrame(s_blue, 640, 480), MediaTime.Zero, MediaTime.FromSeconds(2));
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;

        var thumbnailService = new ProjectThumbnailService(TestShell.Project, TestShell.Editor);
        var hostedVc = new StubVersionControlService($"{BeutlApiApplication.BaseUrl}/api/v3/git/{repoId}.git");
        await thumbnailService.SyncRepositoryThumbnailAsync(
            project, projectFile, hostedVc, api, owner,
            pushUrls: [$"{BeutlApiApplication.BaseUrl}/api/v3/git/{pushRepoId}.git"]);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedPaths, Is.EqualTo(new[] { $"/api/v3/repos/{pushRepoId}/thumbnail" }));
            Assert.That(uploadedBytes, Is.Not.Null);
            Assert.That(uploadedBytes!.Length, Is.GreaterThan(8));
            Assert.That(File.Exists(ProjectThumbnailService.GetPath(projectFile)), Is.True);
        });

        var nonHostedVc = new StubVersionControlService("https://github.com/example/demo.git");
        await thumbnailService.SyncRepositoryThumbnailAsync(
            project, projectFile, nonHostedVc, api, owner,
            pushUrls: ["https://github.com/example/demo.git"]);
        Assert.That(uploadedPaths, Has.Count.EqualTo(1), "Non-Beutl remotes must not trigger thumbnail uploads.");
    }

    [AvaloniaTest]
    public async Task Cloud_project_item_downloads_remote_thumbnail_and_falls_back_when_missing()
    {
        const string repoId = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
        string workspace = TestShell.NewWorkspace("remote-thumbnail");
        string projectFile = Path.Combine(workspace, "sample.bep");
        using (var source = new Bitmap(64, 36))
        {
            ProjectThumbnailService.Save(source, projectFile);
        }
        byte[] pngBytes = await File.ReadAllBytesAsync(ProjectThumbnailService.GetPath(projectFile));
        bool missing = false;

        using var http = new HttpClient(new StubHttpHandler(_ =>
        {
            if (missing)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pngBytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }));
        await using var api = new BeutlApiApplication(
            http, new ExtensionProvider(new ExtensionConfig()), new ViewConfig(), new UpdateConfig(),
            static () => new HttpClient(), persistAuthenticatedUser: _ => { });
        AuthenticatedUser owner = await SignInTestUserAsync(api);
        var service = api.GetResource<HostedGitRepositoryService>();
        var now = DateTimeOffset.UtcNow;

        using var withThumbnail = new RecentProjectItem(Guid.Parse(repoId), "Cloud", now, now);
        Assert.That(withThumbnail.ShowRemotePlaceholder.Value, Is.False);
        await withThumbnail.LoadRemoteThumbnailAsync(service, owner);
        Assert.Multiple(() =>
        {
            Assert.That(withThumbnail.Thumbnail.Value, Is.Not.Null);
            Assert.That(withThumbnail.ShowRemotePlaceholder.Value, Is.False);
        });

        missing = true;
        using var missingThumbnail = new RecentProjectItem(Guid.Parse(repoId), "Missing", now, now);
        await missingThumbnail.LoadRemoteThumbnailAsync(service, owner);
        Assert.Multiple(() =>
        {
            Assert.That(missingThumbnail.Thumbnail.Value, Is.Null);
            Assert.That(missingThumbnail.ShowRemotePlaceholder.Value, Is.True);
        });

        using var noThumbnail = new RecentProjectItem(Guid.Parse(repoId), "None", now, null);
        Assert.That(noThumbnail.ShowRemotePlaceholder.Value, Is.True);

        // Oversized decoded dimensions must be rejected before full bitmap decode.
        byte[] oversizedPng;
        using (var oversized = new Bitmap(1025, 16))
        using (var ms = new MemoryStream())
        {
            Assert.That(oversized.Save(ms, Beutl.Graphics.EncodedImageFormat.Png), Is.True);
            oversizedPng = ms.ToArray();
        }
        pngBytes = oversizedPng;
        missing = false;
        using var oversizedItem = new RecentProjectItem(Guid.Parse(repoId), "Oversized", now, now);
        await oversizedItem.LoadRemoteThumbnailAsync(service, owner);
        Assert.Multiple(() =>
        {
            Assert.That(oversizedItem.Thumbnail.Value, Is.Null);
            Assert.That(oversizedItem.ShowRemotePlaceholder.Value, Is.True);
        });
    }

    private static async Task<AuthenticatedUser> SignInTestUserAsync(BeutlApiApplication api)
    {
        var profile = new Profile(new ProfileResponse
        {
            Id = "owner",
            Name = "owner",
            DisplayName = "Owner",
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, api);
        var user = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, api, DateTime.UtcNow);
        await api.RunInteractiveAuthenticationAsync(_ => Task.FromResult(user), CancellationToken.None);
        return user;
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }

    private sealed class StubVersionControlService(string originUrl) : IProjectVersionControlService
    {
        public RepositoryInfo? Repository => null;
        public Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> GetDiffAsync(string sha, string? path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RemoteInfo>>([new RemoteInfo("origin", originUrl)]);
        public event EventHandler<WorkspaceStatus>? StatusChanged { add { } remove { } }
    }
}
