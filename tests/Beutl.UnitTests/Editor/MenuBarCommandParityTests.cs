using System.Text.RegularExpressions;
using System.Xml.Linq;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

// macOS hides the in-window menu of MainView and shows the native menu of MacWindow instead, so a
// MenuBar command bound in only one of the two files is missing from the menu bar on some platforms.
[TestFixture]
public partial class MenuBarCommandParityTests
{
    private static readonly XNamespace s_avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace s_xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // No command is meant to be in only one menu today. If one ever is, list it here with a comment
    // saying why, instead of letting the menus drift apart.
    private static readonly string[] s_inWindowMenuOnlyCommands = [];
    private static readonly string[] s_nativeMenuOnlyCommands = [];

    [Test]
    public void Mac_native_menu_binds_the_same_menu_bar_commands_as_the_in_window_menu()
    {
        string repositoryRoot = RepositoryPaths.Root;
        XElement inWindowMenu = XDocument.Load(Path.Combine(repositoryRoot, "src/Beutl/Views/MainView.axaml"))
            .Descendants(s_avalonia + "Menu")
            .Single(static element => (string?)element.Attribute(s_xaml + "Name") == "MenuBar");
        XElement nativeMenu = XDocument.Load(Path.Combine(repositoryRoot, "src/Beutl/Views/MacWindow.axaml"))
            .Descendants(s_avalonia + "NativeMenu.Menu")
            .Single();

        string[] inWindowCommands = GetMenuBarCommands(inWindowMenu);
        string[] nativeCommands = GetMenuBarCommands(nativeMenu);

        Assert.Multiple(() =>
        {
            Assert.That(inWindowCommands, Is.Not.Empty, "No MenuBar command bindings were found in MainView.axaml.");
            Assert.That(
                inWindowCommands.Except(nativeCommands, StringComparer.Ordinal),
                Is.EquivalentTo(s_inWindowMenuOnlyCommands),
                "Bound in the MainView.axaml menu but missing from the macOS menu in MacWindow.axaml.");
            Assert.That(
                nativeCommands.Except(inWindowCommands, StringComparer.Ordinal),
                Is.EquivalentTo(s_nativeMenuOnlyCommands),
                "Bound in the macOS menu in MacWindow.axaml but missing from the MainView.axaml menu.");
        });
    }

    private static string[] GetMenuBarCommands(XElement menu)
    {
        return menu.Descendants()
            .Select(static element => (string?)element.Attribute("Command"))
            .OfType<string>()
            .Select(static value => MenuBarBindingRegex().Match(value))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["command"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(@"^\{(?:Compiled)?Binding\s+MenuBar\.(?<command>[A-Za-z0-9_]+)\}$")]
    private static partial Regex MenuBarBindingRegex();
}
