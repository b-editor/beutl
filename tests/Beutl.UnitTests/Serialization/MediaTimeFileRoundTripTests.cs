using System.Text.Json;
using System.Text.Json.Nodes;
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

// Files hold flick counts, and the TimeSpan strings of earlier versions still read, at a whole and an NTSC rate (#2885).
[TestFixture]
public sealed class MediaTimeFileRoundTripTests
{
    private static readonly Rational[] s_rates = [new(30), new(30000, 1001)];

    [TestCaseSource(nameof(s_rates))]
    public void A_file_reads_its_flicks_exactly_and_saves_to_the_same_bytes(Rational rate)
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_bytes");
        // Frame times, which fall between ticks at an NTSC rate, and times off every frame.
        MediaTime[] times =
        [
            .. Enumerable.Range(1, 30).Select(frame => MediaTime.FromFrames(frame, rate)),
            new(1), new(7), new(729_119_977),
        ];
        Scene scene = BuildScene(harness, Pairs(times).Select(pair => (pair.First, pair.First + pair.Second, pair.Second)));
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Dictionary<string, byte[]> saved = ReadFiles(harness.BasePath);
        string elementFile = File.ReadAllText(scene.Children[0].Uri!.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        CoreSerializer.StoreToUri(restored, scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(elementFile, Does.Contain($"\"Start\": {times[0].Flicks}"));
            Assert.That(elementFile, Does.Contain($"\"Length\": {times[1].Flicks}"));
            Assert.That(Describe(restored), Is.EqualTo(Describe(scene)));
            Assert.That(ReadFiles(harness.BasePath), Is.EqualTo(saved));
        });
    }

    // Outside a project an earlier version's strings read rounded up to a flick, which a save then keeps as flicks.
    [TestCaseSource(nameof(s_rates))]
    public void A_file_of_an_earlier_version_saves_the_times_it_read_as_flicks(Rational rate)
    {
        using var harness = new SceneHistoryHarness("beutl_mediatime_earlier");
        // Tick values as an earlier version writes them: frame times rounded down and up, and ticks off every frame.
        long[] ticks = [.. Enumerable.Range(1, 30).SelectMany(frame => FrameTicks(frame, rate)), 1, 7, 12_345_678];
        // An earlier version wrote a length as the distance between its ends in ticks, so the ends are whole ticks.
        Scene scene = BuildScene(harness, Pairs(ticks)
            .Select(pair => (FromTicks(pair.First), FromTicks(pair.First + pair.Second), FromTicks(pair.Second))));
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            CoreSerializer.StoreToUri(scene, scene.Uri!);
        string earlierFile = File.ReadAllText(scene.Children[0].Uri!.LocalPath);

        Scene read = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        CoreSerializer.StoreToUri(read, scene.Uri!);
        string savedFile = File.ReadAllText(read.Children[0].Uri!.LocalPath);
        Scene reread = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(earlierFile, Does.Contain($"\"Start\": \"{TimeSpan.FromTicks(ticks[0]):c}\""));
            Assert.That(Describe(read), Is.EqualTo(Describe(scene)));
            Assert.That(savedFile, Does.Contain($"\"Start\": {read.Children[0].Start.Flicks}"));
            Assert.That(Describe(reread), Is.EqualTo(Describe(scene)));
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

    // Any tick an earlier version wrote reads back to a time that an agent document shows as the same string, and that
    // a file holds as flicks which read back exactly.
    [Test]
    public void Times_read_from_TimeSpan_strings_show_the_same_strings_and_keep_their_flicks()
    {
        var random = new Random(2885);
        long[] ticks =
        [
            0, 1, -1, 7, 333_333, 333_334, -12_345_678, 863_999_999_999, 130_000_000_000_000_000,
            .. Enumerable.Range(0, 500).Select(_ => random.NextInt64(-100_000_000_000_000, 100_000_000_000_000)),
        ];
        var mismatches = new List<string>();
        foreach (long tick in ticks)
        {
            string text = JsonSerializer.Serialize(TimeSpan.FromTicks(tick));
            var time = JsonSerializer.Deserialize<MediaTime>(text);
            using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            {
                if (JsonSerializer.Serialize(time) != text)
                    mismatches.Add($"{tick} shows another string");
            }

            if (JsonSerializer.Deserialize<MediaTime>(JsonSerializer.Serialize(time)) != time)
                mismatches.Add($"{tick} reads other flicks");
        }

        Assert.Multiple(() =>
        {
            Assert.That(mismatches, Is.Empty);
            Assert.That(JsonSerializer.Serialize(new MediaTime(123)), Is.EqualTo("123"));
            Assert.That(JsonSerializer.Deserialize<MediaTime>("123"), Is.EqualTo(new MediaTime(123)));
        });
    }

    // Copies keep the exact time, as files do.
    [Test]
    public void Snapshots_duplicates_and_copies_keep_exact_times()
    {
        var rate = new Rational(30000, 1001);
        using var harness = new SceneHistoryHarness("beutl_mediatime_copies", duration: MediaTime.FromFrames(900, rate));
        Element element = harness.AddElement(MediaTime.FromFrames(37, rate), MediaTime.FromFrames(41, rate));
        Assert.That(element.Start.ToTicks(MidpointRounding.ToNegativeInfinity), Is.Not.EqualTo(element.Start.ToTicks(MidpointRounding.ToPositiveInfinity)));

        Scene snapshot = SceneSnapshot.Create(harness.Scene);
        ObjectRegenerator.Regenerate(element, out Element duplicate);
        string copy = CoreSerializer.SerializeToJsonString(element);
        var pasted = (Element)CoreSerializer.DeserializeFromJsonObject(JsonNode.Parse(copy)!.AsObject(), typeof(Element));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Children.Single().Range, Is.EqualTo(element.Range));
            Assert.That(snapshot.Duration, Is.EqualTo(harness.Scene.Duration));
            Assert.That(duplicate.Range, Is.EqualTo(element.Range));
            Assert.That(copy, Does.Contain($"\"Start\": {element.Start.Flicks}"));
            Assert.That(pasted.Range, Is.EqualTo(element.Range));
        });
    }

    // An element on its own layer for each range, with a keyframe at the given time; the scene spans the elements and
    // has a marker at the second one's start.
    private static Scene BuildScene(SceneHistoryHarness harness, IEnumerable<(MediaTime Start, MediaTime End, MediaTime Key)> ranges)
    {
        Scene scene = harness.Scene;
        (MediaTime Start, MediaTime End, MediaTime Key)[] all = [.. ranges];
        scene.Start = all[0].Start;
        scene.Duration = all[^1].End - scene.Start;
        for (int i = 0; i < all.Length; i++)
        {
            Element element = harness.AddElement(all[i].Start, all[i].End - all[i].Start, i);
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = all[i].Key, Value = 1 });
            var shape = new RectShape();
            shape.Width.SetKeyFrameAnimation(animation);
            element.AddObject(shape);
        }

        scene.Markers.Add(new SceneMarker { Time = all[1].Start });
        return scene;
    }

    private static IEnumerable<(T First, T Second)> Pairs<T>(T[] values)
        => Enumerable.Range(0, values.Length / 2).Select(i => (values[2 * i], values[(2 * i) + 1]));

    private static string[] Describe(Scene scene)
        =>
        [
            $"scene {scene.Start.Flicks} + {scene.Duration.Flicks}, marker {scene.Markers.Single().Time.Flicks}",
            .. scene.Children.OrderBy(element => element.ZIndex).Select(element =>
                $"{element.Start.Flicks} + {element.Length.Flicks}, key "
                + ((RectShape)element.Objects.Single()).Width.GetKeyFrameAnimation()!.KeyFrames.Single().KeyTime.Flicks),
        ];

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
}
