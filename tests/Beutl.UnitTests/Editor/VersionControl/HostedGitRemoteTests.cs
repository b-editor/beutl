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
        Assert.That(environment["GIT_CONFIG_COUNT"], Is.EqualTo("7"));
        Assert.That(environment["GIT_CONFIG_KEY_0"], Is.EqualTo($"http.{RepositoryUrl}.extraheader"));
        Assert.That(environment["GIT_CONFIG_VALUE_0"], Is.EqualTo("Authorization: Bearer temporary-token"));
        Assert.That(environment["GIT_CONFIG_KEY_1"], Is.EqualTo("lfs.customtransfer.beutl-tus.path"));
        Assert.That(environment["GIT_CONFIG_KEY_3"], Is.EqualTo("lfs.customtransfer.beutl-tus.concurrent"));
        Assert.That(environment["GIT_CONFIG_KEY_4"], Is.EqualTo("lfs.customtransfer.beutl-multipart.path"));
        Assert.That(environment["GIT_CONFIG_KEY_6"], Is.EqualTo("lfs.customtransfer.beutl-multipart.concurrent"));
        Assert.That(environment["GIT_TRACE_CURL"], Is.Null);
        Assert.That(environment["GIT_CURL_VERBOSE"], Is.Null);
    }
}
