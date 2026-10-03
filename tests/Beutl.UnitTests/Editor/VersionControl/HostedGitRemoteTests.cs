using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class HostedGitRemoteTests
{
    private const string RepositoryUrl =
        "https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000001.git";

    [Test]
    public void AcceptsOnlyTheHostedRepositoryUrl()
    {
        Assert.That(HostedGitRemote.TryParse(RepositoryUrl, out Guid id), Is.True);
        Assert.That(id, Is.EqualTo(Guid.Parse("00000000-0000-4000-8000-000000000001")));
        Assert.That(HostedGitRemote.TryParse(RepositoryUrl + "/", out _), Is.True);
        Assert.That(HostedGitRemote.TryParse(RepositoryUrl + "?token=x", out _), Is.False);
        Assert.That(HostedGitRemote.TryParse("https://elsewhere.example/api/v3/git/00000000-0000-4000-8000-000000000001.git", out _), Is.False);
        Assert.That(HostedGitRemote.TryParse("https://user@beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000001.git", out _), Is.False);
    }

    [Test]
    public void PassesBearerAndCustomTransferOnlyToTheGitProcess()
    {
        GitCommandOptions result = HostedGitRemote.CreateOptions(
            RepositoryUrl, "temporary-token", GitCommandOptions.Network);
        IReadOnlyDictionary<string, string?> environment = result.EnvironmentOverrides!;
        Assert.That(environment["GIT_CONFIG_COUNT"], Is.EqualTo("5"));
        Assert.That(environment["GIT_CONFIG_KEY_0"], Is.EqualTo($"http.{RepositoryUrl}.extraheader"));
        Assert.That(environment["GIT_CONFIG_VALUE_0"], Is.Empty);
        Assert.That(environment["GIT_CONFIG_KEY_1"], Is.EqualTo($"http.{RepositoryUrl}.extraheader"));
        Assert.That(environment["GIT_CONFIG_VALUE_1"], Is.EqualTo("Authorization: Bearer temporary-token"));
        Assert.That(environment["GIT_CONFIG_KEY_2"], Is.EqualTo("lfs.customtransfer.beutl-tus.path"));
        Assert.That(environment["GIT_CONFIG_KEY_4"], Is.EqualTo("lfs.customtransfer.beutl-tus.concurrent"));
        Assert.That(environment["GIT_TRACE_CURL"], Is.Null);
        Assert.That(environment["GIT_CURL_VERBOSE"], Is.Null);
        Assert.That(environment["GIT_ASKPASS"], Is.Empty);
        Assert.That(environment["SSH_ASKPASS"], Is.Empty);
    }

    [TestCase("Writing objects: 401, unrelated failure", false)]
    [TestCase("The requested URL returned error: 401", false)]
    [TestCase("HTTP/2 401", false)]
    [TestCase("Authentication failed", false)]
    [TestCase("fatal: Authentication failed for 'https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git/'\r\n", true)]
    [TestCase("fatal: unable to access 'https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git/': The requested URL returned error: 401", true)]
    [TestCase("fatal: Authentication failed for 'https://external.example/repository.git/'", false)]
    [TestCase("fatal: unable to access 'https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git/': The requested URL returned error: 403", false)]
    [TestCase("LFS download failed: HTTP 403", true)]
    [TestCase("LFS transfer failed: HTTP 403", true)]
    [TestCase("tus request failed: HTTP 403", true)]
    [TestCase("tus PATCH failed: HTTP 403", true)]
    [TestCase("LFS transfer failed: HTTP 4030", false)]
    [TestCase("The requested URL returned error: 403", false)]
    [TestCase("batch response: Authentication required: Authorization error: https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git/info/lfs/objects/batch\r\n", true)]
    [TestCase("batch response: Authentication required: Authorization error: https://external.example/repo.git/info/lfs/objects/batch", false)]
    [TestCase("batch response: Authentication required: Authorization error: https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git/info/lfs/objects/batch?other=1", false)]
    [TestCase("batch response: Authentication required: Authorization error: https://beutl.beditor.net/api/v3/git/00000000-0000-4000-8000-000000000011.git", false)]
    [TestCase("hook rejected: Authentication required: Authorization error", false)]
    public void AuthenticationRetryRequiresAnAuthenticationError(string stderr, bool expected)
    {
        Assert.That(GitCliVersionControlService.IsHostedAuthenticationFailure(new GitOperationException(128, stderr)),
            Is.EqualTo(expected));
    }
}
