using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineVirtualizationTests
{
    [AvaloniaTest]
    public async Task DeselectingAnOffscreenClipReleasesItsControlAndThumbnailRequests()
    {
        using var host = await TimelineTestHost.CreateAsync("deselect-offscreen", MediaTime.FromSeconds(120),
            [new(MediaTime.Zero, MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.RectShape()))]);
        ElementViewModel clip = host.Model.Elements.Single();
        host.Model.SelectElement(clip);
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Not.Null);
        host.Content.Offset = new Vector(MediaTime.FromSeconds(40).TimeToPixel(1), 0);
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Not.Null, "selected clips retain their existing controls");
        host.Model.ClearSelected();
        await HeadlessTestHelpers.WaitUntilAsync(() => FindView(host, clip) == null);
        Assert.That(clip.GetMissingThumbnailIndices, Is.Null);
    }

    [AvaloniaTest]
    public async Task DistantEditFlashesAfterScrollingRealizesItsControl()
    {
        using var host = await TimelineTestHost.CreateAsync("highlight-offscreen", MediaTime.FromSeconds(120),
            [new(MediaTime.FromSeconds(60), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.RectShape()))]);
        ElementViewModel clip = host.Model.Elements.Single();
        host.Model.ClearSelected();
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Null);
        host.Model.ScrollTo.Execute((clip.Model.Range, 0));
        host.Model.HighlightElement.Execute(clip.Model);
        await HeadlessTestHelpers.WaitUntilAsync(() => FindView(host, clip) is { } view
            && AdornerLayer.GetAdornerLayer(view.border)?.Children.Any(c => AdornerLayer.GetAdornedElement(c) == view.border) == true);
        Assert.That(host.Content.Offset.X, Is.EqualTo(MediaTime.FromSeconds(60).TimeToPixel(1) - 40).Within(0.1));
        await Task.Delay(250);
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.View, nameof(TimelineVirtualizationTests), "distant-edit-highlight");
    }

    private static ElementView? FindView(TimelineTestHost host, ElementViewModel model)
        => host.View.GetVisualDescendants().OfType<ElementView>().SingleOrDefault(view => view.DataContext == model);
}
