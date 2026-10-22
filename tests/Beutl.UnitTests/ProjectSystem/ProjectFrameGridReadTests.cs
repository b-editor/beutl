using System.Globalization;
using System.Text.Json;
using Beutl.Editor.Services;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

// A project's files keep TimeSpan strings, which fall short of most frames. Read into the project, a time less than two
// ticks from one of its frames reads as that frame, so reopened clips meet clips made in the session (#2885).
[TestFixture]
public sealed class ProjectFrameGridReadTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-frame-grid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCaseSource(nameof(s_rates))]
    public void A_clip_of_the_gap_length_moves_into_a_reopened_gap(string rateText)
    {
        (Scene scene, Rational rate) = Reopen(rateText);
        Element clip = NewElement(80, 31, rate);
        scene.AddChild(clip);

        scene.MoveChild(0, MediaTime.FromFrames(30, rate), MediaTime.FromFrames(31, rate), clip);

        AssertFillsTheGap(scene, clip, rate);
    }

    [TestCaseSource(nameof(s_rates))]
    public void A_clip_dragged_a_frame_into_a_reopened_clip_lands_in_the_gap_beside_it(string rateText)
    {
        (Scene scene, Rational rate) = Reopen(rateText);
        Element clip = NewElement(80, 31, rate);
        scene.AddChild(clip);

        scene.MoveChildren(0, MediaTime.FromFrames(29, rate) - clip.Start, [clip]);

        AssertFillsTheGap(scene, clip, rate);
    }

    [TestCaseSource(nameof(s_rates))]
    public void A_clip_placed_against_reopened_clips_touches_them(string rateText)
    {
        (Scene scene, Rational rate) = Reopen(rateText);
        Element clip = NewElement(30, 31, rate);

        Assert.DoesNotThrow(() => scene.AddChild(clip, ElementOverlapHandling.ThrowException));
        AssertFillsTheGap(scene, clip, rate);
    }

    // An element file read again into an open project, as CoreSerializer.PopulateFromUri does, reads on its grid.
    [TestCaseSource(nameof(s_rates))]
    public void An_element_read_again_into_a_reopened_project_reads_on_its_frame_grid(string rateText)
    {
        (Scene scene, Rational rate) = Reopen(rateText);
        Element after = scene.Children.Single(element => element.Start > MediaTime.Zero);

        CoreSerializer.PopulateFromUri(after, after.Uri!);

        Assert.That(after.Range, Is.EqualTo(new TimeRange(MediaTime.FromFrames(61, rate), MediaTime.FromFrames(9, rate))));
    }

    // Saving writes back what it read; a frame written a tick early reads as the frame and is saved on its tick.
    [TestCaseSource(nameof(s_rates))]
    public void A_reopened_project_saves_its_strings_and_a_frame_written_a_tick_early_on_its_tick(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        Element first = NewElement(0, 30, rate);
        Uri uri = Save(rateText, first, NewElement(61, 9, rate));
        Dictionary<string, byte[]> saved = ReadFiles();
        long frameTicks = MediaTime.FromFrames(30, rate).ToTicks(MidpointRounding.ToNegativeInfinity);
        WriteAsEarlier(first, "Length", frameTicks, frameTicks - 1);

        Project reopened = CoreSerializer.RestoreFromUri<Project>(uri);
        Element read = ((Scene)reopened.Items.Single()).Children.Single(element => element.Id == first.Id);
        CoreSerializer.StoreToUri(reopened, uri);

        Assert.Multiple(() =>
        {
            Assert.That(read.Length, Is.EqualTo(MediaTime.FromFrames(30, rate)));
            Assert.That(ReadFiles(), Is.EqualTo(saved));
        });
    }

    // An earlier version rounded a moved clip's start and length down apart: frames 2 to 4 ended at 1,333,332 ticks.
    [Test]
    public void A_clip_an_earlier_version_moved_splits_at_its_last_frame_and_touches_the_next()
    {
        var rate = new Rational(30);
        Element moved = NewElement(2, 2, rate);
        Element next = NewElement(4, 3, rate);
        Uri uri = Save("30", moved, next);
        WriteAsEarlier(moved, "Length", 666_667, 666_666);

        var scene = (Scene)CoreSerializer.RestoreFromUri<Project>(uri).Items.Single();
        Element front = scene.Children.Single(element => element.Id == moved.Id);
        Element after = scene.Children.Single(element => element.Id == next.Id);
        using var history = new HistoryHarness(scene);
        new ElementStructureService(history.History).Split(scene, [front], MediaTime.FromFrames(3, rate));

        Assert.That(scene.Children, Has.Count.EqualTo(3));
        Element back = scene.Children.Single(element => element != front && element != after);
        Assert.Multiple(() =>
        {
            Assert.That(front.Range, Is.EqualTo(new TimeRange(MediaTime.FromFrames(2, rate), MediaTime.FromFrames(1, rate))));
            Assert.That(back.Range.End, Is.EqualTo(MediaTime.FromFrames(4, rate)).And.EqualTo(after.Start));
        });
    }

    // A frame takes the ticks less than two ticks from it; ticks two away from a whole-tick frame keep their time.
    [TestCase(30, 1)]
    [TestCase(30000, 1001)]
    [TestCase(24000, 1001)]
    public void Ticks_less_than_two_ticks_from_a_frame_read_as_that_frame(long numerator, long denominator)
    {
        var rate = new Rational(numerator, denominator);
        var mismatches = new List<string>();
        for (int frame = 0; frame <= 2000; frame++)
        {
            MediaTime time = MediaTime.FromFrames(frame, rate);
            long below = time.ToTicks(MidpointRounding.ToNegativeInfinity);
            long above = time.ToTicks(MidpointRounding.ToPositiveInfinity);
            long[] inside = below == above ? [below - 1, below, below + 1] : [below - 1, below, above, above + 1];
            long[] outside = below == above ? [below - 2, below + 2] : [below - 2, above + 2];
            mismatches.AddRange(inside.Where(ticks => MediaTimeJsonConverter.FromFileTicks(ticks, rate) != time)
                .Select(ticks => $"frame {frame}: {ticks} ticks kept"));
            mismatches.AddRange(outside.Where(ticks => MediaTimeJsonConverter.FromFileTicks(ticks, rate) == time)
                .Select(ticks => $"frame {frame}: {ticks} ticks taken"));
        }

        Assert.That(mismatches, Is.Empty);
    }

    // Times outside a project, such as a template's, keep their ticks rounded up; flick counts are exact everywhere.
    [Test]
    public void Only_strings_read_on_a_project_frame_grid_snap()
    {
        var rate = new Rational(30);
        const string ThirtyOneFrames = "\"00:00:01.0333333\"";
        var outside = JsonSerializer.Deserialize<MediaTime>(ThirtyOneFrames);
        MediaTime inside;
        MediaTime flicks;
        using (MediaTimeJsonConverter.ReadOnFrameGrid(rate))
        {
            inside = JsonSerializer.Deserialize<MediaTime>(ThirtyOneFrames);
            flicks = JsonSerializer.Deserialize<MediaTime>("729119977");
        }

        Assert.Multiple(() =>
        {
            Assert.That(outside, Is.EqualTo(MediaTime.FromTicks(10_333_333, MidpointRounding.ToPositiveInfinity)));
            Assert.That(inside, Is.EqualTo(MediaTime.FromFrames(31, rate)));
            Assert.That(flicks, Is.EqualTo(new MediaTime(729_119_977)));
        });
    }

    // Clips at frames 0 to 30 and 61 to 70 on layer 0, saved and read back into the project: a 31-frame gap.
    private (Scene Scene, Rational Rate) Reopen(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        Uri uri = Save(rateText, NewElement(0, 30, rate), NewElement(61, 9, rate));
        return ((Scene)CoreSerializer.RestoreFromUri<Project>(uri).Items.Single(), rate);
    }

    private Uri Save(string rateText, params Element[] elements)
    {
        var uri = new Uri(Path.Combine(_directory, "project.bep"));
        var project = new Project { Uri = uri };
        project.Variables[ProjectVariableKeys.FrameRate] = rateText;
        var scene = new Scene { Uri = new Uri(Path.Combine(_directory, "scene", "scene.scene")) };
        foreach (Element element in elements)
            scene.AddChild(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, uri);
        return uri;
    }

    // Replaces the ticks this version wrote for a time of the element's file with those an earlier version wrote.
    private static void WriteAsEarlier(Element element, string name, long ticks, long earlierTicks)
    {
        string file = element.Uri!.LocalPath;
        string text = File.ReadAllText(file);
        string earlier = text.Replace(
            $"\"{name}\": \"{TimeSpan.FromTicks(ticks):c}\"", $"\"{name}\": \"{TimeSpan.FromTicks(earlierTicks):c}\"");
        Assert.That(earlier, Is.Not.EqualTo(text));
        File.WriteAllText(file, earlier);
    }

    private Element NewElement(int frame, int count, Rational rate) => new()
    {
        Start = MediaTime.FromFrames(frame, rate),
        Length = MediaTime.FromFrames(count, rate),
        Uri = new Uri(Path.Combine(_directory, "scene", $"{Guid.NewGuid():N}.belm")),
    };

    private static void AssertFillsTheGap(Scene scene, Element clip, Rational rate)
    {
        Element before = scene.Children.Single(element => element != clip && element.Start == MediaTime.Zero);
        Element after = scene.Children.Single(element => element != clip && element.Start > MediaTime.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(clip.ZIndex, Is.Zero);
            Assert.That(clip.Start, Is.EqualTo(MediaTime.FromFrames(30, rate)).And.EqualTo(before.Range.End));
            Assert.That(clip.Range.End, Is.EqualTo(MediaTime.FromFrames(61, rate)).And.EqualTo(after.Start));
        });
    }

    private Dictionary<string, byte[]> ReadFiles()
        => Directory.GetFiles(_directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(_directory, path), File.ReadAllBytes);
}
