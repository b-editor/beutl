using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public partial class VersionControlCommandSurfaceTests
{
    private static readonly string[] s_versionControlCommands =
    [
        "CommitVersion",
        "EnableVersionControl",
    ];

    [Test]
    public void Version_control_commands_are_not_duplicated_by_the_menu_bar()
    {
        string repositoryRoot = FindRepositoryRoot();
        string mainView = File.ReadAllText(Path.Combine(repositoryRoot, "src/Beutl/Views/MainView.axaml"));
        string macWindow = File.ReadAllText(Path.Combine(repositoryRoot, "src/Beutl/Views/MacWindow.axaml"));

        Assert.Multiple(() =>
        {
            Assert.That(
                GetVersionControlMenuCommands(mainView),
                Is.Empty,
                "The version control tab is the only surface for version control actions.");
            Assert.That(
                GetVersionControlMenuCommands(macWindow),
                Is.Empty,
                "The version control tab is the only surface for version control actions.");
        });
    }

    [Test]
    public void Version_control_commands_stay_reachable_as_context_commands()
    {
        string repositoryRoot = FindRepositoryRoot();
        string extension = File.ReadAllText(
            Path.Combine(repositoryRoot, "src/Beutl/Services/PrimitiveImpls/MainViewExtension.cs"));
        string palette = File.ReadAllText(
            Path.Combine(repositoryRoot, "src/Beutl/ViewModels/MenuBarViewModel.Palette.cs"));

        Assert.Multiple(() =>
        {
            foreach (string command in s_versionControlCommands)
            {
                Assert.That(
                    extension,
                    Does.Contain($"new(\"{command}\""),
                    $"{command} must have a ContextCommandDefinition.");
                Assert.That(
                    palette,
                    Does.Contain($"\"{command}\" => {command}"),
                    $"{command} must resolve to its MenuBar command.");
                Assert.That(
                    palette,
                    Does.Not.Contain($"\"MenuBar.{command}\""),
                    $"{command} must not be duplicated by the fallback palette list.");
            }
        });
    }

    private static string[] GetVersionControlMenuCommands(string xaml)
    {
        return CommandBindingRegex()
            .Matches(xaml)
            .Select(static match => match.Groups["command"].Value)
            .Where(static command =>
                command.Contains("VersionControl", StringComparison.Ordinal)
                || command.Contains("CommitVersion", StringComparison.Ordinal)
                || command.Contains("Push", StringComparison.Ordinal)
                || command.Contains("Pull", StringComparison.Ordinal)
                || command.Contains("Branch", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the Beutl repository root.");
    }

    [GeneratedRegex("Command=\"\\{CompiledBinding MenuBar\\.(?<command>[A-Za-z0-9_]+)\\}\"")]
    private static partial Regex CommandBindingRegex();
}
