using System.Diagnostics;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

// The suspended Windows start builds its own command line and environment block. They must match what
// Process builds, which splits back into the same arguments under the C runtime's rules.
[TestFixture]
public class WindowsProcessArgumentsTests
{
    [TestCase(@"C:\Program Files\Git\cmd\git.exe", @"""C:\Program Files\Git\cmd\git.exe""")]
    [TestCase(@"  C:\Git\git.exe  ", @"""C:\Git\git.exe""")]
    [TestCase(@"""C:\Program Files\Git\cmd\git.exe""", @"""C:\Program Files\Git\cmd\git.exe""")]
    public void File_name_is_quoted_unless_it_already_is(string fileName, string expected)
    {
        Assert.That(Build(new ProcessStartInfo(fileName)), Is.EqualTo(expected));
    }

    [TestCase("status", "status")]
    [TestCase("", @"""""")]
    [TestCase("value with spaces", @"""value with spaces""")]
    [TestCase("tab\there", "\"tab\there\"")]
    [TestCase(@"say ""hi""", @"""say \""hi\""""")]
    [TestCase(@"a""b", @"""a\""b""")]
    [TestCase(@"C:\dir\", @"C:\dir\")]
    [TestCase(@"C:\my dir\", @"""C:\my dir\\""")]
    [TestCase(@"a\\""b", @"""a\\\\\""b""")]
    [TestCase(@"a\b c", @"""a\b c""")]
    [TestCase(@"--format=%H%x00", @"--format=%H%x00")]
    public void Arguments_follow_the_c_runtime_quoting_rules(string argument, string expected)
    {
        var startInfo = new ProcessStartInfo("git");
        startInfo.ArgumentList.Add(argument);

        Assert.That(Build(startInfo), Is.EqualTo($"\"git\" {expected}"));
    }

    [Test]
    public void Argument_list_takes_precedence_over_the_arguments_string()
    {
        var startInfo = new ProcessStartInfo("git", "ignored");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=off");

        Assert.Multiple(() =>
        {
            Assert.That(Build(startInfo), Is.EqualTo(@"""git"" -c core.quotepath=off"));
            Assert.That(Build(new ProcessStartInfo("git", "status --short")), Is.EqualTo(@"""git"" status --short"));
        });
    }

    [Test]
    public void Command_line_is_a_terminated_buffer()
    {
        char[] commandLine = WindowsProcessArguments.BuildCommandLine(new ProcessStartInfo("git"));

        Assert.That(commandLine[^1], Is.EqualTo('\0'));
    }

    [Test]
    public void Environment_block_is_sorted_without_regard_to_case_and_ends_with_an_empty_entry()
    {
        var environment = new Dictionary<string, string?>
        {
            ["path"] = @"C:\bin",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["Removed"] = null,
            ["ALLUSERSPROFILE"] = @"C:\ProgramData",
            ["LC_ALL"] = "C",
        };

        char[] block = WindowsProcessArguments.BuildEnvironmentBlock(environment);

        Assert.That(
            new string(block),
            Is.EqualTo("ALLUSERSPROFILE=C:\\ProgramData\0GIT_TERMINAL_PROMPT=0\0LC_ALL=C\0path=C:\\bin\0\0\0"));
    }

    [Test]
    public void Empty_environment_block_is_still_terminated()
    {
        Assert.That(
            WindowsProcessArguments.BuildEnvironmentBlock(new Dictionary<string, string?>()),
            Is.EqualTo(new[] { '\0', '\0' }));
    }

    private static string Build(ProcessStartInfo startInfo)
    {
        char[] commandLine = WindowsProcessArguments.BuildCommandLine(startInfo);
        return new string(commandLine, 0, commandLine.Length - 1);
    }
}
