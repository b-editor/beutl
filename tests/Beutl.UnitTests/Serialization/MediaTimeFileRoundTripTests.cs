using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Serialization;

// Files keep the TimeSpan strings of earlier versions until #2885 raises the format floor, at a whole and an NTSC rate.
[TestFixture]
public sealed class MediaTimeFileRoundTripTests
{
    private static readonly Rational[] s_rates = [new(30), new(30000, 1001)];

    [TestCaseSource(nameof(s_rates))]
    public void A_file_of_an_earlier_version_reads_and_saves_to_the_same_bytes(Rational rate)
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_bytes");
        // Tick values as an earlier version writes them: frame times rounded down and up, and ticks off every frame.
        long[] ticks = [.. Enumerable.Range(1, 30).SelectMany(frame => FrameTicks(frame, rate)), 1, 7, 12_345_678];
        Scene scene = harness.Scene;
        scene.Start = FromTicks(ticks[0]);
        scene.Duration = FromTicks(ticks[0] + ticks[^1]) - scene.Start;
        for (int i = 0; i + 1 < ticks.Length; i += 2)
        {
            Element element = harness.AddElement(FromTicks(ticks[i]), FromTicks(ticks[i] + ticks[i + 1]) - FromTicks(ticks[i]), i);
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = FromTicks(ticks[i + 1]), Value = 1 });
            var shape = new RectShape();
            shape.Width.SetKeyFrameAnimation(animation);
            element.AddObject(shape);
        }

        scene.Markers.Add(new SceneMarker { Time = FromTicks(ticks[3]) });
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Dictionary<string, byte[]> saved = ReadFiles(harness.BasePath);
        string elementFile = File.ReadAllText(scene.Children[0].Uri!.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        CoreSerializer.StoreToUri(restored, scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(elementFile, Does.Contain($"\"Start\": \"{TimeSpan.FromTicks(ticks[0]):c}\""));
            Assert.That(elementFile, Does.Contain($"\"Length\": \"{TimeSpan.FromTicks(ticks[1]):c}\""));
            Assert.That(ReadFiles(harness.BasePath), Is.EqualTo(saved));
        });
    }

    [TestCaseSource(nameof(s_rates))]
    public void Saving_and_reloading_keeps_the_first_and_last_frame_of_each_element(Rational rate)
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_frames");
        for (int i = 0; i < 60; i++)
        {
            harness.AddElement(MediaTime.FromFrames(First(i), rate), MediaTime.FromFrames(Count(i), rate), i);
        }

        Scene restored = SaveAndReload(harness);

        var mismatches = new List<string>();
        foreach (Element element in restored.Children)
        {
            int first = First(element.ZIndex);
            int last = first + Count(element.ZIndex) - 1;
            if (!Shows(element, first, rate) || Shows(element, first - 1, rate)
                || !Shows(element, last, rate) || Shows(element, last + 1, rate))
            {
                mismatches.Add($"frames {first} to {last} became {element.Start.Flicks} + {element.Length.Flicks} flicks");
            }
        }

        Assert.That(mismatches, Is.Empty);

        static int First(int i) => i * 7 + i % 3;
        static int Count(int i) => 1 + i % 5;
    }

    [TestCaseSource(nameof(s_rates))]
    public void Adjacent_elements_stay_adjacent_after_saving_and_reloading(Rational rate)
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_adjacent");
        int frame = 3;
        for (int i = 0; i < 40; i++)
        {
            int count = 1 + i * 7 % 11;
            harness.AddElement(MediaTime.FromFrames(frame, rate), MediaTime.FromFrames(count, rate));
            frame += count;
        }

        Scene restored = SaveAndReload(harness);
        Element[] ordered = [.. restored.Children.OrderBy(element => element.Start)];

        var mismatches = new List<string>();
        for (int i = 1; i < ordered.Length; i++)
        {
            Element outgoing = ordered[i - 1];
            Element incoming = ordered[i];
            if (outgoing.Range.End != incoming.Start
                || !ElementTransitions.AreAdjacent(outgoing, incoming)
                || TrimGroupCollector.CollectRollPairs(restored, [outgoing], incoming.Start) is not { Count: 1 })
            {
                mismatches.Add($"{outgoing.Range.End.Flicks} and {incoming.Start.Flicks} flicks");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    // Any tick an earlier version wrote reads back to a time that writes the same string; an integer reads as flicks.
    [Test]
    public void Times_read_from_TimeSpan_strings_write_the_same_strings()
    {
        var random = new Random(2885);
        long[] ticks =
        [
            0, 1, -1, 7, 333_333, 333_334, -12_345_678, 863_999_999_999, 130_000_000_000_000_000,
            .. Enumerable.Range(0, 500).Select(_ => random.NextInt64(-100_000_000_000_000, 100_000_000_000_000)),
        ];
        var mismatches = new List<long>();
        foreach (long tick in ticks)
        {
            string text = System.Text.Json.JsonSerializer.Serialize(TimeSpan.FromTicks(tick));
            var time = System.Text.Json.JsonSerializer.Deserialize<MediaTime>(text);
            if (System.Text.Json.JsonSerializer.Serialize(time) != text)
                mismatches.Add(tick);
        }

        Assert.Multiple(() =>
        {
            Assert.That(mismatches, Is.Empty);
            Assert.That(System.Text.Json.JsonSerializer.Deserialize<MediaTime>("123"), Is.EqualTo(new MediaTime(123)));
        });
    }

    // Copies that stay in the process keep the exact time, which a file would round to a tick.
    [Test]
    public void Snapshots_duplicates_and_the_clipboard_keep_exact_times()
    {
        var rate = new Rational(30000, 1001);
        using var harness = new SceneHistoryHarness("beutl_mediatime_copies", duration: MediaTime.FromFrames(900, rate));
        Element element = harness.AddElement(MediaTime.FromFrames(37, rate), MediaTime.FromFrames(41, rate));
        Assert.That(element.Start.ToTicks(MidpointRounding.ToNegativeInfinity), Is.Not.EqualTo(element.Start.ToTicks(MidpointRounding.ToPositiveInfinity)));

        Scene snapshot = SceneSnapshot.Create(harness.Scene);
        ObjectRegenerator.Regenerate(element, out Element duplicate);
        string clipboard;
        using (MediaTimeJsonConverter.WriteFlicks())
            clipboard = CoreSerializer.SerializeToJsonString(element);
        var pasted = (Element)CoreSerializer.DeserializeFromJsonObject(
            System.Text.Json.Nodes.JsonNode.Parse(clipboard)!.AsObject(), typeof(Element));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Children.Single().Range, Is.EqualTo(element.Range));
            Assert.That(snapshot.Duration, Is.EqualTo(harness.Scene.Duration));
            Assert.That(duplicate.Range, Is.EqualTo(element.Range));
            Assert.That(clipboard, Does.Contain($"\"Start\": {element.Start.Flicks}"));
            Assert.That(pasted.Range, Is.EqualTo(element.Range));
            Assert.That(
                CoreSerializer.SerializeToJsonString(element),
                Does.Contain($"\"Start\": \"{TimeSpan.FromTicks(element.Start.ToTicks(MidpointRounding.ToNegativeInfinity)):c}\""));
        });
    }

    // The scope belongs to the thread that opened it, so a copy whose clipboard write resumes on another thread must
    // not leave that thread writing flicks into files.
    [Test]
    public void A_clipboard_copy_leaves_its_thread_writing_strings()
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_clipboard_thread");
        Element element = harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var service = new ElementClipboardService(
            harness.History, new ResumingClipboard(), new ElementDuplicateService(harness.History), () => Colors.Magenta);

        string? afterCopy = null;
        var thread = new Thread(() =>
        {
            service.CopyAsync([element]).GetAwaiter().GetResult();
            afterCopy = CoreSerializer.SerializeToJsonString(element);
        });
        thread.Start();
        thread.Join();

        Assert.That(afterCopy, Does.Contain("\"Start\": \"00:00:01\""));
    }

    private static MediaTime FromTicks(long ticks) => MediaTime.FromTicks(ticks, MidpointRounding.ToPositiveInfinity);

    private static IEnumerable<long> FrameTicks(int frame, Rational rate)
    {
        MediaTime time = MediaTime.FromFrames(frame, rate);
        return [time.ToTicks(MidpointRounding.ToNegativeInfinity), time.ToTicks(MidpointRounding.ToPositiveInfinity)];
    }

    private static bool Shows(Element element, int frame, Rational rate)
        => element.Range.Contains(MediaTime.FromFrames(frame, rate));

    private static Scene SaveAndReload(SceneHistoryHarness harness)
    {
        CoreSerializer.StoreToUri(harness.Scene, harness.Scene.Uri!);
        return CoreSerializer.RestoreFromUri<Scene>(harness.Scene.Uri!);
    }

    private static Dictionary<string, byte[]> ReadFiles(string directory)
        => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes);

    private sealed class ResumingClipboard : IClipboardGateway
    {
        public Task<IReadOnlyList<string>> GetFormatsAsync() => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> TryGetStringAsync(string format) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>?> TryGetFilePathsAsync() => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<ReadOnlyMemory<byte>?> TryGetBitmapPngAsync() => Task.FromResult<ReadOnlyMemory<byte>?>(null);

        public async Task<bool> SetAsync(IReadOnlyList<ClipboardEntry> entries)
        {
            await Task.Delay(10).ConfigureAwait(false);
            return true;
        }
    }
}
