using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics3D;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class TimelineFlowInputWarningTests
{
    [AvaloniaTest]
    [TestCase("en-US", false, "Portal’s Count", "Add or enable a Portal")]
    [TestCase("ja-JP", true, "ポータルの［個数］", "ポータルを追加するか有効に")]
    public async Task WarningFollowsEditsAcrossNormalAndCompactClips(string culture, bool light, string hint, string setupHint)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using TimelineTestHost host = await TimelineTestHost.CreateAsync("flow-warning-" + culture,
                MediaTime.FromSeconds(12), theme: light ? ThemeVariant.Light : ThemeVariant.Dark);
            ElementAddResult added = await host.Model.AddElementWithResultAsync(LibraryElementDescription.Create(
                typeof(Scene3D), MediaTime.Zero, MediaTime.FromSeconds(3), 0));
            Assert.That(added.IsSuccess, Is.True);
            Element receiver = added.Items.Single().PrimaryElement;
            Element cube = added.Elements.Single(element => element != receiver);
            if (light) receiver.AccentColor.Value = Beutl.Media.Color.FromRgb(49, 53, 83);

            var editor = (EditViewModel)host.Model.EditorContext;
            var clock = editor.GetRequiredService<IEditorClock>();
            var structure = editor.GetRequiredService<IElementStructureService>();
            var portal = receiver.Objects.OfType<PortalObject>().Single();
            AssertWarning(false);
            int portalCount = portal.Count.Value;
            portal.Count.Value = 0;
            AssertWarning(true);
            portal.Count.Value = portalCount;
            AssertWarning(false);

            portal.IsEnabled.Value = false;
            Assert.That(ToolTip.GetTip(AssertWarning(true)), Does.Contain(setupHint));
            portal.IsEnabled.Value = true;
            AssertWarning(false);

            cube.Start.Value = receiver.Range.End;
            cube.Length.Value = MediaTime.FromSeconds(1);
            AssertWarning(true);
            cube.Start.Value = MediaTime.FromSeconds(1);
            AssertWarning(false);
            clock.Seek(MediaTime.FromSeconds(1));
            AssertWarning(false);
            clock.Seek(MediaTime.FromSeconds(2));
            AssertWarning(false);

            structure.Delete(host.Scene, [cube]);
            Border border = AssertWarning(true);
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineFlowInputWarningTests), $"{culture}-warning");
            host.Window.MouseMove(border.TranslatePoint(new Rect(border.Bounds.Size).Center, host.Window)!.Value);
            ToolTip.SetIsOpen(border, true);
            HeadlessTestHelpers.Render(3);
            if (host.Window.OpenedPopups.LastOrDefault()?.Child is { } popup)
                HeadlessCapture.SaveIfRequested(popup, nameof(TimelineFlowInputWarningTests), $"{culture}-tooltip");
            ToolTip.SetIsOpen(border, false);

            host.Model.ClearSelected();
            host.Window.MouseMove(new Point(5, 5));
            host.Model.Options.Value = host.Model.Options.Value with { Scale = 0.05f };
            AssertCompactWarning(true);
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineFlowInputWarningTests), $"{culture}-compact");
            Rect bounds = host.Model.GetElementBounds(receiver);
            host.Window.MouseMove(host.Timeline.TranslatePoint(bounds.Center, host.Window)!.Value);
            AssertWarning(true);

            Assert.That(await editor.UndoAsync(), Is.True);
            host.Window.MouseMove(new Point(5, 5));
            AssertCompactWarning(false);
            host.Model.Options.Value = host.Model.Options.Value with { Scale = 1 };
            AssertWarning(false);

            ElementView? FindView() => host.Timeline.Children.OfType<ElementView>()
                .SingleOrDefault(view => (view.DataContext as ElementViewModel)?.Model == receiver);

            Border AssertWarning(bool expected)
            {
                HeadlessTestHelpers.Render(3);
                ElementView view = FindView()!;
                Assert.That(view, Is.Not.Null);
                Assert.That(view.FindControl<Control>("flowInputWarningIcon")!.IsVisible, Is.EqualTo(expected));
                Border clip = view.FindControl<Border>("border")!;
                if (expected) Assert.That(ToolTip.GetTip(clip), Does.Contain(hint));
                else Assert.That(ToolTip.GetTip(clip), Is.Null);
                return clip;
            }

            void AssertCompactWarning(bool expected)
            {
                HeadlessTestHelpers.Render(3);
                Assert.That(host.Timeline.Children.OfType<CompactElementLayer>(), Is.Not.Empty);
                Assert.That(FindView(), Is.Null);

                Rect clip = host.Model.GetElementBounds(receiver);
                Point clipTop = host.Timeline.TranslatePoint(new Point(clip.Center.X, clip.Top), host.Window)!.Value;
                double scale = host.Window.RenderScaling;
                int x = (int)Math.Floor(clipTop.X * scale);
                using var frame = host.Window.CaptureRenderedFrame()!;
                // Both samples are inside the clip fill. The upper pixel is covered by the warning marker.
                var marker = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x, (int)Math.Floor((clipTop.Y + 4) * scale)));
                var fill = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x, (int)Math.Floor((clipTop.Y + 8) * scale)));
                Assert.That(marker != fill, Is.EqualTo(expected), "The compact marker should be drawn while input is missing.");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
