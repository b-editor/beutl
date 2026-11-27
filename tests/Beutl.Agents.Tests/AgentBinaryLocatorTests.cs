using Beutl.Agents;

namespace Beutl.Agents.Tests;

public sealed class AgentBinaryLocatorTests
{
    [Test]
    public async Task ResolvesConfiguredPathAndChecksExecutableBit()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-locator-");
        try
        {
            string command = Path.Combine(directory.FullName, OperatingSystem.IsWindows() ? "my-agent.cmd" : "my-agent");
            File.WriteAllText(command, "");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(AgentBinaryLocator.Resolve("my-agent", directory.FullName), Is.Null);
                File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            var descriptor = new AgentCustomProviderConfig("test", "Test agent", "my-agent", [],
                new Dictionary<string, string> { ["PATH"] = directory.FullName }).ToDescriptor();
            var result = await new AgentBinaryLocator().ProbeAsync(descriptor);
            Assert.Multiple(() =>
            {
                Assert.That(result.IsAvailable, Is.True);
                Assert.That(result.ExecutablePath, Is.EqualTo(command));
                Assert.That(AgentBinaryLocator.Resolve(command, null), Is.EqualTo(command));
                Assert.That(AgentBinaryLocator.Resolve("missing", directory.FullName), Is.Null);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Win")]
    public void PrefersExecutableOverCommandShimInSameDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-pathext-");
        try
        {
            string executable = Path.Combine(directory.FullName, "codex.exe");
            File.WriteAllText(executable, "");
            File.WriteAllText(Path.Combine(directory.FullName, "codex.cmd"), "");
            Assert.That(AgentBinaryLocator.Resolve("codex", directory.FullName), Is.EqualTo(executable));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
