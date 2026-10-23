using System.Globalization;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

// The timeline rounds a start and a length to frames separately. In ticks about a third of such pairs ended a tick
// before the next start, which Roll refused; in flicks every pair meets exactly (#2885).
[TestFixture]
public sealed class MediaTimeAdjacencyTests
{
    private static readonly string[] s_rates = ["24", "30000/1001", "30", "60"];

    [TestCaseSource(nameof(s_rates))]
    public void Frames_placed_as_the_timeline_places_them_meet_exactly(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        using var harness = new SceneHistoryHarness("beutl_adjacency", duration: MediaTime.FromSeconds(600));
        var project = new Project();
        project.FrameRate.Value = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        project.Items.Add(harness.Scene);
        var service = new ElementResizeService(harness.History);
        var mismatches = new List<string>();
        for (int first = 0; first < 60; first++)
        {
            int count = 1 + first % 7;
            Element outgoing = harness.AddElement(Snap(first, rate), Snap(count, rate));
            Element incoming = harness.AddElement(Snap(first + count, rate), Snap(3, rate));

            bool meet = outgoing.Range.End == incoming.Start.Value;
            bool adjacent = ElementTransitions.AreAdjacent(outgoing, incoming);
            bool rolled = service.Roll(harness.Scene, [new ElementTrimPair(outgoing, incoming)], MediaTime.FromFrames(1, rate));
            if (!meet || !adjacent || !rolled)
                mismatches.Add($"frames {first} + {count}: meet {meet}, adjacent {adjacent}, rolled {rolled}");

            harness.Scene.Children.Remove(outgoing);
            harness.Scene.Children.Remove(incoming);
        }

        Assert.That(mismatches, Is.Empty);
    }

    // A pointer position a third of a frame off, snapped to the nearest frame as the timeline does.
    private static MediaTime Snap(int frames, Rational rate)
        => MediaTime.FromSeconds((frames + 0.3) / rate.ToDouble()).RoundToFrame(rate, MidpointRounding.AwayFromZero);
}
