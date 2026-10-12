using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.FFmpegIpc.Protocol.Messages;
using NUnit.Framework;

namespace Beutl.UnitTests.Extensions.FFmpeg;

// The holder exposes static singletons, so each test uses its own key to avoid cross-test leakage.
[TestFixture]
public class FFmpegOptionsCachesTests
{
    private static Task<OptionsQueryResult<T>> Ok<T>(params T[] items)
        => Task.FromResult(new OptionsQueryResult<T>(items, Degraded: false));

    private static string UniqueKey(string codec) => $"{codec}\0{Guid.NewGuid():N}.mp4";

    [Test]
    public void EachCache_ReturnsSameInstanceAcrossAccesses()
    {
        // Each property must return the same instance every time, or editors wouldn't share a cache.
        Assert.That(ReferenceEquals(FFmpegOptionsCaches.AudioFormats, FFmpegOptionsCaches.AudioFormats), Is.True);
        Assert.That(ReferenceEquals(FFmpegOptionsCaches.PixelFormats, FFmpegOptionsCaches.PixelFormats), Is.True);
        Assert.That(ReferenceEquals(FFmpegOptionsCaches.SampleRates, FFmpegOptionsCaches.SampleRates), Is.True);
        Assert.That(ReferenceEquals(FFmpegOptionsCaches.EncoderOptions, FFmpegOptionsCaches.EncoderOptions), Is.True);
    }

    [Test]
    public async Task AudioFormats_SharedAcrossLogicalEditors_DedupesWorkerQuery()
    {
        // Two editor code paths asking for the same codec must share one factory invocation
        // (single-flight is now cross-instance).
        int calls = 0;
        string key = UniqueKey("aac");
        var gate = new TaskCompletionSource<OptionsQueryResult<FFmpegAudioEncoderSettings.AudioFormat>>();

        Task<OptionsQueryResult<FFmpegAudioEncoderSettings.AudioFormat>> first =
            FFmpegOptionsCaches.AudioFormats.GetOrQueryAsync(
                key, () => { calls++; return gate.Task; });
        Task<OptionsQueryResult<FFmpegAudioEncoderSettings.AudioFormat>> second =
            FFmpegOptionsCaches.AudioFormats.GetOrQueryAsync(
                key, () => { calls++; return Ok(FFmpegAudioEncoderSettings.AudioFormat.Default); });

        gate.SetResult(new OptionsQueryResult<FFmpegAudioEncoderSettings.AudioFormat>(
            [FFmpegAudioEncoderSettings.AudioFormat.Default], Degraded: false));
        OptionsQueryResult<FFmpegAudioEncoderSettings.AudioFormat>[] results =
            await Task.WhenAll(first, second);

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(results[0].Items, Is.EquivalentTo(results[1].Items));
    }

    [Test]
    public async Task PixelFormats_PopulatedEntry_IsServedByTryGetCached()
    {
        string key = UniqueKey("h264");
        await FFmpegOptionsCaches.PixelFormats.GetOrQueryAsync(
            key, () => Ok(new PixelFormatInfo()));

        Assert.That(
            FFmpegOptionsCaches.PixelFormats.TryGetCached(key, out PixelFormatInfo[]? cached),
            Is.True);
        Assert.That(cached, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task SampleRates_PopulatedEntry_IsServedByTryGetCached()
    {
        string key = UniqueKey("aac");
        await FFmpegOptionsCaches.SampleRates.GetOrQueryAsync(key, () => Ok(48000));

        Assert.That(
            FFmpegOptionsCaches.SampleRates.TryGetCached(key, out int[]? cached),
            Is.True);
        Assert.That(cached, Is.EqualTo(new[] { 48000 }));
    }
}
