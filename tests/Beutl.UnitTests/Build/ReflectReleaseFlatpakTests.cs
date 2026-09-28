using System.Diagnostics;
using System.Text.Json;

namespace Beutl.UnitTests.Build;

[TestFixture]
public sealed class ReflectReleaseFlatpakTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task RegistrationPayload_RequiresFlatpakAndPreservesItsPublishedUrl(bool includeFlatpak)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("The release workflow uses Bash and jq.");
        const string version = "2.0.0";
        string[] names =
        [
            "beutl-setup.exe", "beutl-standalone-setup.exe",
            "beutl-arm64-setup.exe", "beutl-standalone-arm64-setup.exe",
            "beutl_2.0.0_amd64.deb", "Beutl-linux-x64-2.0.0.zip",
            "Beutl-linux-x64-standalone-2.0.0.zip", "Beutl.osx_x64.app.zip", "Beutl.osx_arm64.app.zip",
        ];
        if (includeFlatpak) names = [.. names, "Beutl-2.0.0.flatpak"];
        var assets = names.Select(name => new
        {
            name,
            browser_download_url = "https://downloads.example.test/published/" + name + "?token=registered"
        });

        string directory = Path.Combine(Path.GetTempPath(), "beutl-reflect-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "output");
        try
        {
            string[] lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "reflect-release.yml"));
            int step = Array.FindIndex(lines, line => line.Trim() == "- name: Build asset payload");
            Assert.That(step, Is.GreaterThanOrEqualTo(0));
            int run = Array.FindIndex(lines, step, line => line.Trim() == "run: |");
            Assert.That(run, Is.GreaterThan(step));
            string script = string.Join('\n', lines.Skip(run + 1)
                .TakeWhile(line => string.IsNullOrWhiteSpace(line) || line.StartsWith("          ", StringComparison.Ordinal))
                .Select(line => line.Length >= 10 ? line[10..] : line));
            var startInfo = new ProcessStartInfo("bash")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-euo", "pipefail", "-c", script },
            };
            startInfo.Environment["VER"] = version;
            startInfo.Environment["ASSETS"] = JsonSerializer.Serialize(assets);
            startInfo.Environment["GITHUB_OUTPUT"] = output;
            using var process = Process.Start(startInfo)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (!includeFlatpak)
            {
                Assert.That(process.ExitCode, Is.Not.Zero);
                Assert.That(await stdout, Does.Contain("::error::missing asset: Beutl-2.0.0.flatpak"));
                Assert.That(File.Exists(output), Is.False, "A missing bundle must fail before existing registrations are deleted.");
                return;
            }

            Assert.That(process.ExitCode, Is.Zero, await stderr);
            string[] result = await File.ReadAllLinesAsync(output);
            Assert.That(result[0], Is.EqualTo("body<<EOF"));
            using var json = JsonDocument.Parse(result[1]);
            var registered = json.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            var flatpak = registered.Single(asset => asset.GetProperty("type").GetString() == "flatpak");
            Assert.Multiple(() =>
            {
                Assert.That(registered, Has.Length.EqualTo(10));
                Assert.That(flatpak.GetProperty("id").GetString(), Is.EqualTo("beutl-2.0.0-linux-x64-flatpak"));
                Assert.That(flatpak.GetProperty("version").GetString(), Is.EqualTo(version));
                Assert.That(flatpak.GetProperty("os").GetString(), Is.EqualTo("linux"));
                Assert.That(flatpak.GetProperty("arch").GetString(), Is.EqualTo("x64"));
                Assert.That(flatpak.GetProperty("standalone").GetBoolean(), Is.True);
                Assert.That(flatpak.GetProperty("url").GetString(),
                    Is.EqualTo("https://downloads.example.test/published/Beutl-2.0.0.flatpak?token=registered"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".github", "workflows", "reflect-release.yml")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the release workflow.");
    }
}
