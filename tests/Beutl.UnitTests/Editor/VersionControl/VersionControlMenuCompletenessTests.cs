using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public partial class VersionControlMenuCompletenessTests
{
    private static readonly string[] s_expectedMenuCommands =
    [
        "CommitVersion",
        "EnableVersionControl",
    ];

    [Test]
    public void Native_menu_mirrors_version_control_commands_and_context_definitions()
    {
        string repositoryRoot = FindRepositoryRoot();
        string mainView = File.ReadAllText(Path.Combine(repositoryRoot, "src/Beutl/Views/MainView.axaml"));
        string macWindow = File.ReadAllText(Path.Combine(repositoryRoot, "src/Beutl/Views/MacWindow.axaml"));
        string extension = File.ReadAllText(
            Path.Combine(repositoryRoot, "src/Beutl/Services/PrimitiveImpls/MainViewExtension.cs"));
        string palette = File.ReadAllText(
            Path.Combine(repositoryRoot, "src/Beutl/ViewModels/MenuBarViewModel.Palette.cs"));

        string[] mainCommands = GetVersionControlMenuCommands(mainView);
        string[] nativeCommands = GetVersionControlMenuCommands(macWindow);

        Assert.Multiple(() =>
        {
            Assert.That(mainCommands, Is.EqualTo(s_expectedMenuCommands));
            Assert.That(nativeCommands, Is.EqualTo(mainCommands));
            foreach (string command in s_expectedMenuCommands)
            {
                Assert.That(
                    CountCommand(mainView, command),
                    Is.EqualTo(1),
                    $"{command} must appear exactly once in MainView.");
                Assert.That(
                    CountCommand(macWindow, command),
                    Is.EqualTo(1),
                    $"{command} must appear exactly once in the macOS native menu.");
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

    private static int CountCommand(string xaml, string command)
    {
        return CommandBindingRegex()
            .Matches(xaml)
            .Count(match => match.Groups["command"].Value == command);
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
