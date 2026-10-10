using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public sealed class LiveMcpTokenStoreTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("live-mcp-token-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public void Token_survives_stale_settings_rewrites_and_different_legacy_snapshots()
    {
        string token = LiveMcpTokenStore.GetOrCreate(_directory);
        string original = File.ReadAllText(Path.Combine(_directory, LiveMcpTokenStore.FileName));
        SaveLegacyToken(_directory, "stale-settings-token");
        string restored = LiveMcpTokenStore.GetOrCreate(_directory, "stale-memory-token");
        Assert.Multiple(() =>
        {
            Assert.That(token, Does.Match("^[0-9A-F]{32}$"));
            Assert.That(restored, Is.EqualTo(token));
            Assert.That(File.ReadAllText(Path.Combine(_directory, LiveMcpTokenStore.FileName)), Is.EqualTo(original));
        });
    }

    [Test]
    public void Migration_prefers_saved_legacy_token_over_a_stale_configuration_snapshot()
    {
        SaveLegacyToken(_directory, "existing-saved-token");
        Assert.That(LiveMcpTokenStore.GetOrCreate(_directory, "old-snapshot-token"), Is.EqualTo("existing-saved-token"));
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{}");
        Assert.That(LiveMcpTokenStore.GetOrCreate(_directory), Is.EqualTo("existing-saved-token"));
    }

    [Test]
    public void Migration_uses_loaded_legacy_token_if_a_settings_save_has_already_omitted_it()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{}");
        Assert.That(LiveMcpTokenStore.GetOrCreate(_directory, "loaded-token"), Is.EqualTo("loaded-token"));
    }

    [TestCase("{")]
    [TestCase("{}")]
    [TestCase("{\"schemaVersion\":1,\"token\":\"\"}")]
    [TestCase("{\"schemaVersion\":2,\"token\":\"unknown-format\"}")]
    public void Invalid_store_is_not_replaced_with_a_different_token(string content)
    {
        string path = Path.Combine(_directory, LiveMcpTokenStore.FileName);
        File.WriteAllText(path, content);
        Assert.Throws<InvalidDataException>(() => LiveMcpTokenStore.GetOrCreate(_directory, "fallback-token"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(content));
    }

    [Test]
    public void Invalid_legacy_settings_do_not_silently_generate_a_replacement_token()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{");
        Assert.Throws<InvalidDataException>(() => LiveMcpTokenStore.GetOrCreate(_directory));
        Assert.That(File.Exists(Path.Combine(_directory, LiveMcpTokenStore.FileName)), Is.False);
    }

    [Test]
    public void Unix_store_and_lock_are_private_to_the_owner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are unavailable on Windows.");
            return;
        }
        LiveMcpTokenStore.GetOrCreate(_directory);
        UnixFileMode tokenMode = File.GetUnixFileMode(Path.Combine(_directory, LiveMcpTokenStore.FileName));
        UnixFileMode lockMode = File.GetUnixFileMode(Path.Combine(_directory, LiveMcpTokenStore.LockFileName));
        Assert.Multiple(() =>
        {
            Assert.That(tokenMode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(lockMode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Concurrent_processes_use_one_token_for_a_profile(bool migrate)
    {
        if (migrate) SaveLegacyToken(_directory, "existing-profile-token");
        const int Count = 4;
        using FileStream lease = new(Path.Combine(_directory, LiveMcpTokenStore.LockFileName),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task[] workers = Enumerable.Range(0, Count).Select(index => TestWorkerProgram.RunAsync(
            TestWorkerProgram.LiveMcpTokenWorkerArgument, _directory, migrate ? $"snapshot-{index}" : "",
            Path.Combine(_directory, $"ready-{index}"), Path.Combine(_directory, $"result-{index}"))).ToArray();
        try
        {
            Stopwatch deadline = Stopwatch.StartNew();
            while (Enumerable.Range(0, Count).Any(index => !File.Exists(Path.Combine(_directory, $"ready-{index}"))))
            {
                if (workers.Any(task => task.IsFaulted)) await Task.WhenAll(workers);
                Assert.That(deadline.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "Workers did not reach token acquisition.");
                await Task.Delay(20);
            }
            Assert.That(File.Exists(Path.Combine(_directory, LiveMcpTokenStore.FileName)), Is.False,
                "Token creation must wait while another process owns the profile lock.");
        }
        finally { lease.Dispose(); }
        await Task.WhenAll(workers);
        string[] fingerprints = Enumerable.Range(0, Count)
            .Select(index => File.ReadAllText(Path.Combine(_directory, $"result-{index}"))).ToArray();
        string expected = Fingerprint(LiveMcpTokenStore.GetOrCreate(_directory));
        Assert.That(fingerprints, Is.All.EqualTo(expected));
        if (migrate) Assert.That(expected, Is.EqualTo(Fingerprint("existing-profile-token")));
    }

    internal static void RunWorker(string directory, string legacyToken, string readyPath, string resultPath)
    {
        File.WriteAllText(readyPath, "ready");
        string token = LiveMcpTokenStore.GetOrCreate(directory, legacyToken);
        File.WriteAllText(resultPath, Fingerprint(token)); // Do not put credentials in worker output.
    }

    private static string Fingerprint(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void SaveLegacyToken(string directory, string token)
        => File.WriteAllText(Path.Combine(directory, "settings.json"),
            new JsonObject { ["AiAgent"] = new JsonObject { ["LiveMcpToken"] = token } }.ToJsonString());
}
