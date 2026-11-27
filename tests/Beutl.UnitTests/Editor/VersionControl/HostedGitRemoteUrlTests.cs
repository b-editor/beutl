using System.Net;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.UnitTests.Api;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class HostedGitRemoteUrlTests
{
    private const string Id = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
    private const string ProductionOrigin = "https://beutl.beditor.net";

    [Test]
    public void Parses_case_and_trailing_slashes_and_builds_dashboard_links()
    {
        string remote = $"https://BEUTL.BEDITOR.NET/API/v3/git/{Id.ToUpperInvariant()}.GIT/";
        Assert.That(HostedGitRemoteUrl.TryParse(remote, ProductionOrigin + "/", out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.RepositoryId, Is.EqualTo(Id));
            Assert.That(parsed.GetRepositoryUri().AbsoluteUri, Is.EqualTo($"{ProductionOrigin}/dashboard/repositories/{Id}"));
            Assert.That(parsed.GetRepositoryUri("feature/日本語#1").AbsoluteUri,
                Is.EqualTo($"{ProductionOrigin}/dashboard/repositories/{Id}?ref=feature%2F%E6%97%A5%E6%9C%AC%E8%AA%9E%231"));
        });
    }

    [Test]
    public void Custom_origin_requires_the_same_scheme_host_and_port()
    {
        const string origin = "http://localhost:3001";
        Assert.That(HostedGitRemoteUrl.TryParse($"{origin}/api/v3/git/{Id}.git", origin, out var parsed), Is.True);
        Assert.That(parsed!.GetRepositoryUri().AbsoluteUri, Is.EqualTo($"{origin}/dashboard/repositories/{Id}"));
        Assert.That(HostedGitRemoteUrl.TryParse($"https://localhost:3001/api/v3/git/{Id}.git", origin, out _), Is.False);
        Assert.That(HostedGitRemoteUrl.TryParse($"http://localhost:3002/api/v3/git/{Id}.git", origin, out _), Is.False);
    }

    [Test]
    public void An_advertised_git_origin_keeps_dashboard_links_on_the_web_origin()
    {
        const string git = "https://git.example.invalid/api/v3/git/" + Id + ".git";
        var repository = new GitRepositorySummary(Id, "Sample project", git, default, default);

        Assert.That(HostedGitRemoteUrl.TryParseAdvertised(git, repository, out var remote), Is.True);
        Assert.That(remote!.GetRepositoryUri("main").AbsoluteUri,
            Is.EqualTo($"{BeutlApiApplication.BaseUrl}/dashboard/repositories/{Id}?ref=main"));
        Assert.That(HostedGitRemoteUrl.TryParseAdvertised(git.Replace("git.example", "other.example", StringComparison.Ordinal),
            repository, out _), Is.False);
        Assert.That(HostedGitRemoteUrl.TryParseAdvertised(git, repository with { Id = Guid.NewGuid().ToString("D") }, out _),
            Is.False);
    }

    [Test]
    public async Task A_temporary_API_failure_can_retry_on_the_next_remote_refresh()
    {
        const string origin = "https://git.example.invalid/api/v3/git/" + Id + ".git";
        var repository = HostedGitRepositoryServiceTests.Repository() with { Url = origin };
        var timeout = TimeSpan.FromSeconds(3);
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using var http = new HttpClient(new DelegateHttpHandler(_ =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                firstRequest.TrySetResult();
                return firstReply.Task;
            }
            return Task.FromResult(HostedGitRepositoryServiceTests.Json(repository));
        }));
        await using BeutlApiApplication api = HostedGitRepositoryServiceTests.CreateApp(http);
        ApiTestAuthentication.SignIn(api);
        var lookup = new HostedGitRemoteLookup();
        var failure = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0;
        HostedGitRemoteUrl? recognized = null;
        void Post(Action action) => (Interlocked.Increment(ref callbacks) == 1 ? failure : recovery).SetResult(action);
        void Refresh() => lookup.Update(origin, api, CancellationToken.None, Post, remote => recognized = remote);

        Refresh();
        await firstRequest.Task.WaitAsync(timeout);
        Refresh();
        Assert.That(requests, Is.EqualTo(1), "An in-flight lookup is shared across refreshes.");
        firstReply.SetResult(HostedGitRepositoryServiceTests.Json(new { message = "Temporarily unavailable" },
            HttpStatusCode.ServiceUnavailable));
        (await failure.Task.WaitAsync(timeout))();
        Assert.That(recognized, Is.Null);

        Refresh();
        (await recovery.Task.WaitAsync(timeout))();
        Assert.That(recognized?.RepositoryId, Is.EqualTo(Id));
        Refresh();
        Assert.That(requests, Is.EqualTo(2), "The successful lookup is reused.");
    }

    [TestCase("https://github.com/api/v3/git/" + Id + ".git")]
    [TestCase(ProductionOrigin + "/api/v3/git/00000000-0000-0000-0000-000000000000.git")]
    [TestCase(ProductionOrigin + "/api/v3/git/" + Id + ".git/objects")]
    [TestCase(ProductionOrigin + "/api/v3/git/" + Id + ".git?secret=1")]
    [TestCase(ProductionOrigin + "/api/v3/git/" + Id + ".git#fragment")]
    [TestCase("https://git:secret@beutl.beditor.net/api/v3/git/" + Id + ".git")]
    [TestCase("git@beutl.beditor.net:api/v3/git/" + Id + ".git")]
    public void Non_hosted_and_credential_bearing_urls_are_rejected(string url)
        => Assert.That(HostedGitRemoteUrl.TryParse(url, ProductionOrigin, out _), Is.False);
}
