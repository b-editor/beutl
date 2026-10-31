using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// The editor's maximum time is where the last element ends. A bulk removal raises Detached for every element after
// removing them all, so the maximum must follow the elements that are left, whatever order they leave in.
[TestFixture]
public sealed class EditorClockMaximumTimeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Shortening_twenty_thousand_elements_updates_the_maximum_without_rescanning(bool sharedEnd)
    {
        var scene = new Scene();
        Element[] elements = Enumerable.Range(1, 20000).Select(i => Clip(sharedEnd ? 20000 : i)).ToArray();
        scene.Children.AddRange(elements);
        using var clock = new EditorClockImpl(scene);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = elements.Length - 1; i >= 0; i--)
            elements[i].Length.Value = MediaTime.Zero;
        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.Zero));
        TestContext.Out.WriteLine($"Shorten 20000 clips (sharedEnd={sharedEnd}): {watch.Elapsed.TotalMilliseconds:F1} ms");
        foreach (Element element in elements) element.Length.Value = MediaTime.FromSeconds(10);
        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
        scene.Children.Clear();
        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void Clearing_the_scene_resets_the_maximum_time(bool laterFirst)
    {
        var scene = new Scene();
        Element early = Clip(10);
        Element late = Clip(20);
        scene.Children.AddRange(laterFirst ? new[] { late, early } : new[] { early, late });
        using var clock = new EditorClockImpl(scene);

        scene.Children.Clear();

        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Removing_a_range_lowers_the_maximum_time_to_the_elements_left()
    {
        var scene = new Scene();
        using var clock = new EditorClockImpl(scene);
        scene.Children.AddRange([Clip(5), Clip(10), Clip(20)]);
        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(20)));

        scene.Children.RemoveRange(1, 2);

        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
    }

    [Test]
    public void Removing_an_element_that_ends_earlier_keeps_the_maximum_time()
    {
        var scene = new Scene();
        Element early = Clip(10);
        scene.Children.AddRange([early, Clip(20)]);
        using var clock = new EditorClockImpl(scene);

        scene.Children.Remove(early);

        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(20)));
    }

    [Test]
    public void Removing_one_of_two_elements_that_end_last_keeps_the_maximum_time()
    {
        var scene = new Scene();
        Element first = Clip(20);
        scene.Children.AddRange([first, Clip(20), Clip(10)]);
        using var clock = new EditorClockImpl(scene);

        scene.Children.Remove(first);

        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(20)));
    }

    [Test]
    public void Adding_an_element_raises_the_maximum_time_only_when_it_ends_later()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(20));
        using var clock = new EditorClockImpl(scene);

        scene.Children.Add(Clip(10));
        MediaTime afterShorter = clock.MaximumTime.Value;
        scene.Children.Add(Clip(30));

        Assert.Multiple(() =>
        {
            Assert.That(afterShorter, Is.EqualTo(MediaTime.FromSeconds(20)));
            Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(30)));
        });
    }

    [Test]
    public void Shortening_the_last_element_lowers_the_maximum_time()
    {
        var scene = new Scene();
        Element last = Clip(20);
        scene.Children.AddRange([Clip(10), last]);
        using var clock = new EditorClockImpl(scene);

        last.Length.Value = MediaTime.FromSeconds(5);

        Assert.That(clock.MaximumTime.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
    }

    private static Element Clip(int endSeconds)
        => new() { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(endSeconds) } };
}
