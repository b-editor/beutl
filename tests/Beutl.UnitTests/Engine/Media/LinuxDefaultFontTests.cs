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

    [Test]
    public void TimedOutQuery_UsesSkiaDefaultAndStopsTheProcess()
    {
        string pidFile = Path.Combine(_directory, "pid");
        string executable = WriteFcMatch($"printf '%s' \"$$\" > {Quote(pidFile)}\nexec /bin/sleep 60");
        var elapsed = Stopwatch.StartNew();
        SKTypeface face = DefaultFontResolver.Resolve(executable, timeoutMilliseconds: 500);

        Assert.Multiple(() =>
        {
            Assert.That(face, Is.SameAs(SKTypeface.Default));
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            int pid = int.Parse(File.ReadAllText(pidFile));
            Assert.That(() => Process.GetProcessById(pid), Throws.TypeOf<ArgumentException>());
        });
    }

    private async Task RunWorkerAsync(bool familyFirst, string expected)
    {
        await TestWorkerProgram.RunAsync(start =>
        {
            // Keep the .NET host available while fc-match can only be found in this directory.
            start.FileName = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet"));
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
