using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Configuration;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

[TestFixture]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxDefaultFontTests
{
    private string _directory = null!;
    private string _font = null!;
    private string _calls = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-default-font-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _font = Path.Combine(_directory, "Roboto Regular.ttf");
        _calls = Path.Combine(_directory, "calls");
        using Stream font = typeof(LinuxDefaultFontTests).Assembly
            .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.Roboto-Regular.ttf")!;
        using Stream file = File.Create(_font);
        font.CopyTo(file);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(true)]
    [TestCase(false)]
    public async Task FamilyAndManager_UseTheSamePathMatchOnce(bool familyFirst)
    {
        WriteFcMatch($"printf '%s' {Quote(_font)}\nprintf 'called\\n' >> {Quote(_calls)}");
        await RunWorkerAsync(familyFirst, "Roboto");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MissingPathCommand_StillInitializesText(bool familyFirst)
        => await RunWorkerAsync(familyFirst, "fallback");

    [TestCase("")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Query_PreservesLeadingAndTrailingPathSpaces(string newline)
    {
        const string relativeFont = " Roboto Regular.ttf ";
        File.Copy(_font, Path.Combine(_directory, relativeFont));
        File.Delete(_font);
        WriteFcMatch($"printf '%s%s' {Quote(relativeFont)} {Quote(newline)}\nprintf 'called\\n' >> {Quote(_calls)}");
        await RunWorkerAsync(familyFirst: true, "Roboto");
    }

    [Test]
    public void MissingExecutable_UsesSkiaDefault()
    {
        Assert.That(DefaultFontResolver.Resolve(Path.Combine(_directory, "missing")), Is.SameAs(SKTypeface.Default));
    }

    [Test]
    public void NonExecutableCommand_UsesSkiaDefault()
    {
        string executable = WriteFcMatch($"printf '%s' {Quote(_font)}");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.That(DefaultFontResolver.Resolve(executable), Is.SameAs(SKTypeface.Default));
    }

    [Test]
    public void FailedQuery_DiscardsItsFontOutput()
    {
        string executable = WriteFcMatch($"printf '%s' {Quote(_font)}\nexit 1");
        Assert.That(DefaultFontResolver.Resolve(executable), Is.SameAs(SKTypeface.Default));
    }

    [TestCase("")]
    [TestCase("missing.ttf")]
    [TestCase("invalid.ttf")]
    public void UnusableOutput_UsesSkiaDefault(string fileName)
    {
        File.WriteAllText(Path.Combine(_directory, "invalid.ttf"), "not a font");
        string output = fileName.Length == 0 ? "" : Path.Combine(_directory, fileName);
        string executable = WriteFcMatch($"printf '%s' {Quote(output)}");
        Assert.That(DefaultFontResolver.Resolve(executable), Is.SameAs(SKTypeface.Default));
    }

    [Test]
    public void Query_DrainsStandardErrorAndReadsTheWholeFontPath()
    {
        string executable = WriteFcMatch($"""
            i=0
            while [ "$i" -lt 4096 ]; do
                printf 'fontconfig diagnostic output\n' >&2
                i=$((i + 1))
            done
            printf '%s' {Quote(_font)}
            """);
        SKTypeface face = DefaultFontResolver.Resolve(executable);
        try
        {
            Assert.That(face.FamilyName, Is.EqualTo("Roboto"));
        }
        finally
        {
            if (!ReferenceEquals(face, SKTypeface.Default))
                face.Dispose();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TimedOutQuery_UsesSkiaDefaultAndStopsTheProcess(bool parentExitsFirst)
    {
        string identityFile = Path.Combine(_directory, "process");
        string sleeper = $"""
            IFS= read -r identity < /proc/$$/stat
            printf '%s' "$identity" > {Quote(identityFile)}
            exec /bin/sleep 60
            """;
        string body = parentExitsFirst
            ? $"/bin/sh -c {Quote(sleeper)} &\nexit 0"
            : sleeper;
        string executable = WriteFcMatch(body);
        try
        {
            var elapsed = Stopwatch.StartNew();
            SKTypeface face = DefaultFontResolver.Resolve(executable, timeoutMilliseconds: 500);

            Assert.Multiple(() =>
            {
                Assert.That(face, Is.SameAs(SKTypeface.Default));
                Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
                Assert.That(SpinWait.SpinUntil(() => !IsRecordedProcessRunning(identityFile), 1000), Is.True,
                    "The original sleep process must stop even if its parent has already exited.");
            });
        }
        finally
        {
            // A failing regression check must not leave the sleeper running.
            if (File.Exists(identityFile) && IsRecordedProcessRunning(identityFile))
            {
                int pid = int.Parse(File.ReadAllText(identityFile).Split(' ')[0]);
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static bool IsRecordedProcessRunning(string identityFile)
    {
        string recorded = File.ReadAllText(identityFile);
        string pid = recorded.Split(' ')[0];
        string current;
        try
        {
            current = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (IOException)
        {
            return false;
        }

        // Compare start time as well as PID; an exited PID may already have been reused.
        string[] before = recorded[(recorded.LastIndexOf(')') + 2)..].Split(' ');
        string[] after = current[(current.LastIndexOf(')') + 2)..].Split(' ');
        return before[19] == after[19] && after[0] != "Z";
    }

    private async Task RunWorkerAsync(bool familyFirst, string expected)
    {
        await TestWorkerProgram.RunAsync(start =>
        {
            // Keep the .NET host available while fc-match can only be found in this directory.
            start.FileName = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet"));
            start.WorkingDirectory = _directory;
            start.Environment["PATH"] = _directory;
            start.Environment["BEUTL_HOME"] = Path.Combine(_directory, "home");
        }, TestWorkerProgram.DefaultFontWorkerArgument, familyFirst.ToString(), expected, _calls);
    }

    internal static void RunWorker(bool familyFirst, string expected, string calls)
    {
        GlobalConfiguration.Instance.FontConfig.FontDirectories.Clear();
        string expectedName = expected == "fallback" ? SKTypeface.Default.FamilyName : expected;
        if (familyFirst)
            _ = FontFamily.Default;
        else
            _ = FontManager.Instance.DefaultTypeface;

        FontManager manager = FontManager.Instance;
        var text = new TextBlock();
        using var formatted = new FormattedText();
        Assert.Multiple(() =>
        {
            Assert.That(manager.DefaultTypeface.FontFamily.Name, Is.EqualTo(expectedName));
            Assert.That(FontFamily.Default, Is.SameAs(manager.DefaultTypeface.FontFamily));
            Assert.That(manager.IsRegistered(FontFamily.Default), Is.True);
            Assert.That(text.FontFamily.CurrentValue, Is.SameAs(FontFamily.Default));
            Assert.That(formatted.Font, Is.SameAs(FontFamily.Default));
            int count = File.Exists(calls) ? File.ReadAllLines(calls).Length : 0;
            Assert.That(count, Is.EqualTo(expected == "fallback" ? 0 : 1));
        });
    }

    private string WriteFcMatch(string body)
    {
        string executable = Path.Combine(_directory, "fc-match");
        File.WriteAllText(executable, """
            #!/bin/sh
            if [ "$#" -ne 2 ] || [ "$1" != "--format" ] || [ "$2" != "%{file}" ]; then
                exit 2
            fi

            """ + body + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return executable;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
