using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

[TestFixture]
public sealed class AgentToolkitMcpServerLocatorTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        // Outside the repo, so the source-project parent walk cannot find
        // src/Beutl.AgentToolkit.Mcp and production behavior is exercised.
        _tempRoot = Path.Combine(Path.GetTempPath(), "beutl-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Test]
    public void Returns_null_instead_of_a_dotnet_run_guess_when_nothing_is_found()
    {
        Assert.That(AgentToolkitMcpServerLocator.ResolveDefault(_tempRoot), Is.Null);
    }

    [Test]
    public void Prefers_a_published_dll_next_to_the_app()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "Beutl.AgentToolkit.Mcp.dll"), "");

        AgentToolkitMcpServerCommand? command = AgentToolkitMcpServerLocator.ResolveDefault(_tempRoot);

        Assert.That(command, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(command!.Command, Is.EqualTo("dotnet"));
            Assert.That(command.Arguments, Is.EqualTo(new[]
            {
                Path.Combine(_tempRoot, "Beutl.AgentToolkit.Mcp.dll"),
            }));
            Assert.That(command.Source, Is.EqualTo("published dll"));
        });
    }

    [Test]
    public void Probes_the_AgentToolkitMcp_subdirectory()
    {
        string subdirectory = Path.Combine(_tempRoot, "AgentToolkitMcp");
        Directory.CreateDirectory(subdirectory);
        File.WriteAllText(Path.Combine(subdirectory, "Beutl.AgentToolkit.Mcp.dll"), "");

        AgentToolkitMcpServerCommand? command = AgentToolkitMcpServerLocator.ResolveDefault(_tempRoot);

        Assert.That(command, Is.Not.Null);
        Assert.That(command!.Arguments, Is.EqualTo(new[]
        {
            Path.Combine(subdirectory, "Beutl.AgentToolkit.Mcp.dll"),
        }));
    }

    [Test]
    public void Falls_back_to_dotnet_run_only_when_the_source_checkout_exists()
    {
        string project = Path.Combine(_tempRoot, "src", "Beutl.AgentToolkit.Mcp");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Beutl.AgentToolkit.Mcp.csproj"), "<Project />");

        AgentToolkitMcpServerCommand? command = AgentToolkitMcpServerLocator.ResolveDefault(
            Path.Combine(_tempRoot, "nested", "bin"));

        Assert.That(command, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(command!.Command, Is.EqualTo("dotnet"));
            Assert.That(command.Arguments[0], Is.EqualTo("run"));
            Assert.That(command.Source, Is.EqualTo("source project"));
        });
    }
}
