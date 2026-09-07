using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

public class AiUploadBytesTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-ai-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task ReadWithinAsync_ReturnsWhatTheFileHoldsWhenItFitsAsync()
    {
        string path = await WriteAsync("fits.bin", 4096);

        byte[] bytes = await AiUploadBytes.ReadWithinAsync(path, 4096, default);

        Assert.That(bytes, Is.EqualTo(await File.ReadAllBytesAsync(path)));
    }

    [Test]
    public async Task ReadWithinAsync_RefusesAFileLargerThanWhatTheRequestIsAllowedAsync()
    {
        // 名前を付けるために読むが、送れる量は先に分かっている。それを超えて
        // 読んでも使い道はなく、選んだあとに膨らんだ 1 つのファイルで残りの
        // メモリを使い切る。
        string path = await WriteAsync("too-large.bin", 8192);

        Assert.ThrowsAsync<AiFileTooLargeException>(
            async () => await AiUploadBytes.ReadWithinAsync(path, 4096, default));
    }

    [Test]
    public async Task ReadWithinAsync_StopsAtTheLimitEvenWhenTheFileGrowsWhileItIsReadAsync()
    {
        // 書かれている最中のファイルは、長さを見たあとに伸びる。長さだけを
        // 信じると、上限をいくらでも超えて読み続けることになる。
        string path = Path.Combine(_directory, "growing.bin");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        await using (FileStream growing = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            growing.Seek(0, SeekOrigin.End);
            await growing.WriteAsync(new byte[256 * 1024]);
            await growing.FlushAsync();

            Assert.ThrowsAsync<AiFileTooLargeException>(
                async () => await AiUploadBytes.ReadWithinAsync(path, 2048, default));
        }
    }

    private async Task<string> WriteAsync(string name, int length)
    {
        string path = Path.Combine(_directory, name);
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}
